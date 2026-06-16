#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Frahan.Masonry.Quarry.Processing;
using Frahan.Masonry.Quarry.Ingestion;

namespace Frahan.StonePack.Harness
{
    /// <summary>
    /// Headless GPR pipeline profiler (no Rhino.Inside: ingest + kriging are pure
    /// Core array math). Answers: what costs the compute (ingest vs kriging), and
    /// how the fracture-surface kriging trades "passes through the picks" against
    /// "smooth / not multilayered". Usage:
    ///   Frahan.StonePack.Harness --gpr &lt;gpr_data_dir&gt; [k] [gridRes]
    /// </summary>
    internal static class GprProfile
    {
        public static int Run(string[] args)
        {
            if (args.Length < 2 || !Directory.Exists(args[1]))
            { Console.Error.WriteLine("usage: --gpr <gpr_data_dir> [k] [gridRes]"); return 2; }
            string dir = args[1];
            int k = args.Length > 2 ? int.Parse(args[2]) : 3;
            int gridRes = args.Length > 3 ? int.Parse(args[3]) : 26;

            var files = Directory.GetFiles(dir, "*.DT").OrderBy(f => f).ToList();
            Console.WriteLine($"GPR profile: {files.Count} .DT lines in {dir}  k={k} gridRes={gridRes}");

            // Botticino marble data -> the marble preset (v=0.10 m/ns, marble short-reflector continuity).
            // granite_160 (v=0.12, 40-trace gate) would mis-convert depth and miss marble's short veins.
            string presetKey = args.Length > 5 ? args[5] : "marble_600";
            if (!GprPresets.TryGet(presetKey, out var preset))
            { Console.Error.WriteLine("preset missing: " + presetKey); return 3; }
            Console.WriteLine("preset=" + presetKey + " (" + preset.Label + ", v=" + preset.VelocityMNsPerNs + " m/ns)");

            // ---- ingest, timed per stage ----
            double tLoad = 0, tGrid = 0, tRun = 0, tExtract = 0;
            var recs = new List<(int axis, double length, List<double[]> picks)>();
            var swAll = Stopwatch.StartNew();
            foreach (var file in files)
            {
                var sw = Stopwatch.StartNew();
                GprRadargram rg; try { rg = GprFileReader.Load(file, null); } catch { continue; }
                tLoad += sw.Elapsed.TotalMilliseconds; sw.Restart();
                if (rg.TraceCount < 2) continue;
                var proc = new RadargramProcessor(); var fx = new FractureExtractor();
                preset.Apply(proc, fx);
                double v = preset.VelocityMNsPerNs;
                double[,] B; double dtNs, dx;
                try { B = RadargramProcessor.ToGrid(rg, out dtNs, out dx); } catch { continue; }
                tGrid += sw.Elapsed.TotalMilliseconds; sw.Restart();
                double[,] energy = proc.Run(B, dtNs, dx, v);
                tRun += sw.Elapsed.TotalMilliseconds; sw.Restart();
                var picks = fx.Extract(energy, dtNs, dx, v);
                tExtract += sw.Elapsed.TotalMilliseconds;
                int ntr = B.GetLength(1);
                int axis = Path.GetFileName(file).ToUpperInvariant().Contains("TA") ? 1 : 0;
                var pl = new List<double[]>(); double maxTx = 0;
                foreach (var p in picks)
                { double tx = rg.Traces[Math.Min(p.TraceIndex, ntr - 1)].X; pl.Add(new[]{tx,p.Energy,p.DepthMetres}); if (tx>maxTx) maxTx=tx; }
                recs.Add((axis, Math.Max(maxTx, (ntr-1)*dx), pl));
            }
            double tIngest = swAll.Elapsed.TotalMilliseconds;

            // ---- bidirectional layout -> pick cloud (x,y,depth) ----
            int lonCount = recs.Count(r=>r.axis==0), traCount = recs.Count(r=>r.axis==1);
            double lonLen = recs.Where(r=>r.axis==0).Select(r=>r.length).DefaultIfEmpty(0).Max();
            double traLen = recs.Where(r=>r.axis==1).Select(r=>r.length).DefaultIfEmpty(0).Max();
            bool bidir = lonCount>0 && traCount>0;
            double lonSpace = bidir && lonCount>1 ? traLen/(lonCount-1) : 2.0;
            double traSpace = bidir && traCount>1 ? lonLen/(traCount-1) : 2.0;
            var X=new List<double>(); var Y=new List<double>(); var D=new List<double>(); var E=new List<double>();
            int lonIdx=0, traIdx=0;
            foreach (var rec in recs)
            { double perp = rec.axis==0 ? (lonIdx++)*lonSpace : (traIdx++)*traSpace;
              foreach (var p in rec.picks){ double tx=p[0], depth=p[2];
                if (rec.axis==0){ X.Add(tx); Y.Add(perp);} else { X.Add(perp); Y.Add(tx);} D.Add(depth); E.Add(p[1]); } }
            int n = D.Count;
            Console.WriteLine($"picks={n} ({lonCount} LON + {traCount} TRA, bidir={bidir})  extent x[{(n>0?X.Min():0):F2},{(n>0?X.Max():0):F2}] y[{(n>0?Y.Min():0):F2},{(n>0?Y.Max():0):F2}] depth[{(n>0?D.Min():0):F2},{(n>0?D.Max():0):F2}]");
            Console.WriteLine($"INGEST {tIngest:F0} ms total  | load {tLoad:F0} | toGrid {tGrid:F0} | proc.Run(migrate+Hilbert) {tRun:F0} | extract {tExtract:F0}");

            // ---- bed assignment (k-planes) ----
            var Xa=X.ToArray(); var Ya=Y.ToArray(); var Da=D.ToArray(); var Ea=E.ToArray();
            var swK = Stopwatch.StartNew();
            int[] lab = ClusterByDippingBeds(Xa, Ya, Da, k, out int kEff);
            Console.WriteLine($"k-planes clustering: {swK.Elapsed.TotalMilliseconds:F0} ms -> {kEff} beds");

            // ---- robust-kriging comparison: outlier rejection (drop picks far from the bed's
            // robust plane) before kriging, at a fixed smoothing nugget. ----
            Console.WriteLine();
            Console.WriteLine("clip(sigma) | nug | bed | dip | kept/total | THROUGH-PICKS RMS(m) | roughness RMS-from-plane(m)");
            double nf2 = 0.15;
            foreach (double clip in new[]{ 99.0, 3.0, 2.5, 2.0 })
            {
                for (int c=0;c<kEff;c++)
                {
                    var ix = Enumerable.Range(0,n).Where(i=>lab[i]==c).ToList();
                    if (ix.Count<4) continue;
                    // iterative plane fit + sigma-clip
                    double[] plane = null;
                    for (int it=0; it<4; it++)
                    {
                        var px=ix.Select(i=>Xa[i]).ToArray(); var py=ix.Select(i=>Ya[i]).ToArray(); var pd=ix.Select(i=>Da[i]).ToArray();
                        plane=FitPlaneLs(px,py,pd);
                        var res=new double[pd.Length]; for(int i=0;i<pd.Length;i++) res[i]=pd[i]-(plane[0]*px[i]+plane[1]*py[i]+plane[2]);
                        double m=res.Average(); double sd=Math.Sqrt(Math.Max(1e-9,res.Select(r=>(r-m)*(r-m)).Sum()/Math.Max(1,res.Length-1)));
                        if (clip>=99) break;
                        var keep=new List<int>(); for(int i=0;i<ix.Count;i++) if(Math.Abs(res[i]-m)<=clip*sd) keep.Add(ix[i]);
                        if (keep.Count==ix.Count || keep.Count<4) { ix=keep.Count>=4?keep:ix; break; } ix=keep;
                    }
                    int total = Enumerable.Range(0,n).Count(i=>lab[i]==c);
                    var cx=ix.Select(i=>Xa[i]).ToArray(); var cy=ix.Select(i=>Ya[i]).ToArray(); var cd=ix.Select(i=>Da[i]).ToArray();
                    plane=FitPlaneLs(cx,cy,cd);
                    var resid=new double[cd.Length]; for(int i=0;i<cd.Length;i++) resid[i]=cd[i]-(plane[0]*cx[i]+plane[1]*cy[i]+plane[2]);
                    double rmean=resid.Average(); double rsill=Math.Max(1e-9, resid.Select(r=>(r-rmean)*(r-rmean)).Sum()/Math.Max(1,resid.Length-1));
                    double extent=Math.Max(cx.Max()-cx.Min(), cy.Max()-cy.Min()); double range=Math.Max(1.0,0.6*extent);
                    Kriging kr; try { kr=new Kriging(cx,cy,resid,range,rsill,nf2*rsill+1e-12); } catch { continue; }
                    double tp=0; for(int i=0;i<cx.Length;i++){ double pr=plane[0]*cx[i]+plane[1]*cy[i]+plane[2]+kr.Predict(cx[i],cy[i]).Mean; tp+=(pr-cd[i])*(pr-cd[i]); }
                    double tpRms=Math.Sqrt(tp/cx.Length);
                    double x0=cx.Min(),x1=cx.Max(),y0=cy.Min(),y1=cy.Max(); double ss=0; int cnt=0;
                    for(int gj=0;gj<gridRes;gj++) for(int gi=0;gi<gridRes;gi++){
                        double gx=x0+(x1-x0)*gi/(gridRes-1), gy=y0+(y1-y0)*gj/(gridRes-1);
                        double rr=kr.Predict(gx,gy).Mean; ss+=rr*rr; cnt++; }
                    double surfRms=Math.Sqrt(ss/Math.Max(1,cnt));
                    double dip=Math.Atan(Math.Sqrt(plane[0]*plane[0]+plane[1]*plane[1]))*180/Math.PI;
                    Console.WriteLine($"  {(clip>=99?"none":clip.ToString("F1")),9} | {nf2:G2} | {c} | {dip,5:F1} | {cx.Length,4}/{total,-4} | {tpRms,18:F4} | {surfRms,27:F4}");
                }
                Console.WriteLine();
            }

            // ---- DUMP the FINAL-config surfaces (2-sigma clip + nugget 0.15, matching the component) for a
            // headless matplotlib render: picks.csv + bed{c}.csv (grid z, 'nan' where masked out). ----
            string outDir = args.Length > 4 ? args[4] : Path.Combine(Path.GetTempPath(), "gpr_render");
            Directory.CreateDirectory(outDir);
            using (var pw = new StreamWriter(Path.Combine(outDir, "picks.csv")))
            {
                pw.WriteLine("x,y,z,bed");
                for (int i = 0; i < n; i++) pw.WriteLine($"{Xa[i]:F4},{Ya[i]:F4},{-Da[i]:F4},{lab[i]}");
            }
            for (int c = 0; c < kEff; c++)
            {
                var ix = Enumerable.Range(0,n).Where(i=>lab[i]==c).ToList();
                if (ix.Count < 4) continue;
                var cx=ix.Select(i=>Xa[i]).ToArray(); var cy=ix.Select(i=>Ya[i]).ToArray(); var cd=ix.Select(i=>Da[i]).ToArray();
                ClipToRobustPlane(ref cx, ref cy, ref cd, 2.0, 4);
                var plane=FitPlaneLs(cx,cy,cd);
                var resid=new double[cd.Length]; for(int i=0;i<cd.Length;i++) resid[i]=cd[i]-(plane[0]*cx[i]+plane[1]*cy[i]+plane[2]);
                double rmean=resid.Average(); double rsill=Math.Max(1e-9, resid.Select(r=>(r-rmean)*(r-rmean)).Sum()/Math.Max(1,resid.Length-1));
                double x0=cx.Min(),x1=cx.Max(),y0=cy.Min(),y1=cy.Max();
                double extent=Math.Max(x1-x0,y1-y0); double range=Math.Max(1.0,0.6*extent);
                double cell=extent/gridRes; double maskR=Math.Max(2.0*cell, 0.6*extent);
                double rLo=resid.Min(), rHi=resid.Max(); double rMar=Math.Max(0.15,0.5*(rHi-rLo)); rLo-=rMar; rHi+=rMar;
                Kriging kr; try { kr=new Kriging(cx,cy,resid,range,rsill,0.15*rsill+1e-9); } catch { continue; }
                using (var bw = new StreamWriter(Path.Combine(outDir, $"bed{c}.csv")))
                {
                    bw.WriteLine($"{x0:F4},{x1:F4},{y0:F4},{y1:F4},{gridRes}");
                    for (int gj=0; gj<gridRes; gj++) { var row=new List<string>();
                        for (int gi=0; gi<gridRes; gi++){
                            double gx=x0+(x1-x0)*gi/(gridRes-1), gy=y0+(y1-y0)*gj/(gridRes-1);
                            double nd=double.MaxValue; for(int i=0;i<cx.Length;i++){double dx=cx[i]-gx,dy=cy[i]-gy;double dd=dx*dx+dy*dy;if(dd<nd)nd=dd;}
                            if (Math.Sqrt(nd)>maskR){ row.Add("nan"); continue; }
                            double mr=kr.Predict(gx,gy).Mean; mr=Math.Max(rLo,Math.Min(rHi,mr));
                            double depth=plane[0]*gx+plane[1]*gy+plane[2]+mr;
                            row.Add((-depth).ToString("F4")); }
                        bw.WriteLine(string.Join(",", row)); }
                }
            }
            Console.WriteLine($"DUMP -> {outDir} (picks.csv + bed*.csv, 2-sigma clip + nug 0.15)");
            return 0;
        }

        // ---- copies of the GH-component math (so the harness profiles the real algorithm) ----
        private static int[] ClusterByDippingBeds(double[] X, double[] Y, double[] D, int k, out int kEff)
        {
            int n = D.Length;
            int[] lab = ClusterByDepth(D, k, out kEff);
            int K = kEff; if (K < 2) return lab;
            for (int iter=0; iter<12; iter++)
            {
                var planes=new double[K][];
                for(int c=0;c<K;c++){ var ix=new List<int>(); for(int i=0;i<n;i++) if(lab[i]==c) ix.Add(i);
                    planes[c]= ix.Count>=3 ? FitPlaneLs(ix.Select(i=>X[i]).ToArray(),ix.Select(i=>Y[i]).ToArray(),ix.Select(i=>D[i]).ToArray()) : null; }
                bool moved=false;
                for(int i=0;i<n;i++){ int best=lab[i]; double bd=double.MaxValue;
                    for(int c=0;c<K;c++){ if(planes[c]==null) continue; double pred=planes[c][0]*X[i]+planes[c][1]*Y[i]+planes[c][2]; double dd=Math.Abs(D[i]-pred); if(dd<bd){bd=dd;best=c;} }
                    if(best!=lab[i]){lab[i]=best;moved=true;} }
                if(!moved) break;
            }
            var meanD=new double[K]; var cnt=new int[K];
            for(int i=0;i<n;i++){meanD[lab[i]]+=D[i];cnt[lab[i]]++;}
            for(int c=0;c<K;c++) meanD[c]=cnt[c]>0?meanD[c]/cnt[c]:double.MaxValue;
            var order=Enumerable.Range(0,K).OrderBy(c=>meanD[c]).ToArray(); var remap=new int[K];
            for(int r=0;r<K;r++) remap[order[r]]=r; for(int i=0;i<n;i++) lab[i]=remap[lab[i]];
            return lab;
        }

        private static int[] ClusterByDepth(double[] depth, int k, out int kEff)
        {
            int n=depth.Length; var order=Enumerable.Range(0,n).OrderBy(i=>depth[i]).ToArray();
            if(k<1){ var gaps=new double[n-1]; for(int i=0;i<n-1;i++) gaps[i]=depth[order[i+1]]-depth[order[i]];
                var sg=(double[])gaps.Clone(); Array.Sort(sg); double med=sg[sg.Length/2]; double thr=Math.Max(2.5*med,1e-6);
                k=1; foreach(var g in gaps) if(g>thr) k++; k=Math.Max(1,Math.Min(8,k)); }
            var centers=new double[k]; for(int c=0;c<k;c++) centers[c]=depth[order[(int)((c+0.5)/k*(n-1))]];
            var lab=new int[n];
            for(int it=0;it<25;it++){ for(int i=0;i<n;i++){int best=0;double bd=double.MaxValue;for(int c=0;c<k;c++){double dd=Math.Abs(depth[i]-centers[c]);if(dd<bd){bd=dd;best=c;}}lab[i]=best;}
                var sum=new double[k];var cnt=new int[k];for(int i=0;i<n;i++){sum[lab[i]]+=depth[i];cnt[lab[i]]++;}
                bool moved=false;for(int c=0;c<k;c++)if(cnt[c]>0){double nc=sum[c]/cnt[c];if(Math.Abs(nc-centers[c])>1e-9)moved=true;centers[c]=nc;}if(!moved)break; }
            kEff=k; return lab;
        }

        private static void ClipToRobustPlane(ref double[] cx, ref double[] cy, ref double[] cd, double sigma, int iters)
        {
            for (int it=0; it<iters; it++)
            {
                var p=FitPlaneLs(cx,cy,cd); var res=new double[cd.Length];
                for(int i=0;i<cd.Length;i++) res[i]=cd[i]-(p[0]*cx[i]+p[1]*cy[i]+p[2]);
                double m=res.Average(); double sd=Math.Sqrt(Math.Max(1e-9, res.Select(r=>(r-m)*(r-m)).Sum()/Math.Max(1,res.Length-1)));
                double thr=sigma*sd; var kx=new List<double>();var ky=new List<double>();var kd=new List<double>();
                for(int i=0;i<cd.Length;i++) if(Math.Abs(res[i]-m)<=thr){kx.Add(cx[i]);ky.Add(cy[i]);kd.Add(cd[i]);}
                if(kd.Count<4||kd.Count==cd.Length) break;
                cx=kx.ToArray();cy=ky.ToArray();cd=kd.ToArray();
            }
        }

        private static double[] FitPlaneLs(double[] x, double[] y, double[] d)
        {
            double Sxx=0,Sxy=0,Sx=0,Syy=0,Sy=0,S1=x.Length,Sxd=0,Syd=0,Sd=0;
            for(int i=0;i<x.Length;i++){Sxx+=x[i]*x[i];Sxy+=x[i]*y[i];Sx+=x[i];Syy+=y[i]*y[i];Sy+=y[i];Sxd+=x[i]*d[i];Syd+=y[i]*d[i];Sd+=d[i];}
            double[,] M={{Sxx,Sxy,Sx},{Sxy,Syy,Sy},{Sx,Sy,S1}}; double[] rhs={Sxd,Syd,Sd};
            for(int col=0;col<3;col++){int piv=col;for(int r=col+1;r<3;r++)if(Math.Abs(M[r,col])>Math.Abs(M[piv,col]))piv=r;
                if(Math.Abs(M[piv,col])<1e-12) return new[]{0.0,0.0,d.Average()};
                if(piv!=col){for(int j=0;j<3;j++){var t=M[col,j];M[col,j]=M[piv,j];M[piv,j]=t;}var tr=rhs[col];rhs[col]=rhs[piv];rhs[piv]=tr;}
                for(int r=col+1;r<3;r++){double f=M[r,col]/M[col,col];for(int j=col;j<3;j++)M[r,j]-=f*M[col,j];rhs[r]-=f*rhs[col];}}
            var ab=new double[3];for(int r=2;r>=0;r--){double s=rhs[r];for(int j=r+1;j<3;j++)s-=M[r,j]*ab[j];ab[r]=s/M[r,r];}
            return ab;
        }
    }
}
