// Example 29 - Live-edge plain-sawn flooring (2D edge matching)
// Source of the C# Script component embedded in 29_liveedge_floor.gh.
//
// Pipeline: irregular offcut outline -> classify (live vs sawn) -> orient -> match + scribe into
// predefined smooth river seams -> brick-bond staggered layup. Output: vertex-coloured Meshes.
//
// Component I/O:
//   input  R  (Run, boolean)        gate; true solves on open
//   output M  (Meshes)              vertex-coloured boards; previews in any shaded viewport
//
// RunScript body:
//   bool run=false; try{ run = Convert.ToBoolean(R); }catch{}
//   if(!run){ M=null; return; }
//   M = BuildFloor(313131);
//
// To run on real offcuts, replace MakeBoard with your closed outline polylines and feed them through
// Classify -> Extract -> the layup in BuildFloor unchanged.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using Rhino.Geometry;

public static class LiveEdgeFloor
{
    // ---- deterministic LCG (no Random, so the result is reproducible) ----
    static ulong _s = 0;
    static double Rnd(){ unchecked { _s = _s*6364136223846793005UL + 1442695040888963407UL; } return ((_s>>33)&0x7fffffff)/2147483647.0; }

    // ---- synthesise one irregular offcut: skewed quad, two curvy live edges + two straight sawn ends ----
    static Point3d[] MakeBoard(){
        double Wb=95+Rnd()*60, Hb=54+Rnd()*12;
        Point3d BL=new Point3d(0,(Rnd()-0.5)*4,0), BR=new Point3d(Wb,(Rnd()-0.5)*4,0);
        Point3d TR=new Point3d(Wb+(Rnd()-0.5)*6,Hb+(Rnd()-0.5)*4,0), TL=new Point3d((Rnd()-0.5)*6,Hb+(Rnd()-0.5)*4,0);
        int M=40;
        Point3d[] Live(Point3d A,Point3d B,double amp){
            Vector3d dir=B-A; double len=dir.Length; dir.Unitize(); Vector3d nrm=new Vector3d(-dir.Y,dir.X,0);
            double p1=Rnd()*6.28,p2=Rnd()*6.28,p3=Rnd()*6.28; double k1=1+Math.Floor(Rnd()*2),k2=2+Math.Floor(Rnd()*2),k3=3+Math.Floor(Rnd()*3);
            Point3d[] pts=new Point3d[M];
            for(int i=0;i<M;i++){double t=(double)i/(M-1);double off=amp*(Math.Sin(Math.PI*k1*t+p1)+0.5*Math.Sin(Math.PI*k2*t+p2)+0.22*Math.Sin(Math.PI*k3*t+p3));off+=(Rnd()-0.5)*amp*0.22;off*=Math.Sin(Math.PI*t)*0.55+0.45;pts[i]=A+dir*(len*t)+nrm*off;}
            pts[0]=A;pts[M-1]=B;return pts;
        }
        double ampv=3+Rnd()*3; var bottom=Live(BL,BR,ampv); var top=Live(TR,TL,ampv);
        Point3d[] Str(Point3d A,Point3d B,int n){var a=new Point3d[n];for(int i=0;i<n;i++)a[i]=A+(B-A)*((double)i/(n-1));return a;}
        var right=Str(BR,TR,6); var left=Str(TL,BL,6);
        var loop=new List<Point3d>(); loop.AddRange(bottom); for(int i=1;i<right.Length;i++)loop.Add(right[i]); for(int i=1;i<top.Length;i++)loop.Add(top[i]); for(int i=1;i<left.Length;i++)loop.Add(left[i]);
        return loop.ToArray();
    }

    static Point3d[] Resample(Point3d[] loop,int N){
        var pts=loop.ToList(); pts.Add(loop[0]); var cum=new double[pts.Count]; double total=0;
        for(int i=1;i<pts.Count;i++){total+=pts[i].DistanceTo(pts[i-1]);cum[i]=total;}
        var outp=new Point3d[N];
        for(int k=0;k<N;k++){double d=total*k/N;int j=1;while(j<pts.Count&&cum[j]<d)j++;if(j>=pts.Count)j=pts.Count-1;double t=(d-cum[j-1])/Math.Max(1e-9,(cum[j]-cum[j-1]));outp[k]=pts[j-1]+(pts[j]-pts[j-1])*t;}
        return outp;
    }

    // ---- classify: the two longest straight runs are the sawn ends; their 4 endpoints are the corners ----
    static void Classify(Point3d[] loop, out int[] corners, out bool[] live){
        int n=loop.Length;int w=2;double[] turn=new double[n];
        for(int i=0;i<n;i++){Vector3d u=loop[i]-loop[(i-w+n)%n];Vector3d v=loop[(i+w)%n]-loop[i];if(u.Length<1e-9||v.Length<1e-9){turn[i]=0;continue;}u.Unitize();v.Unitize();turn[i]=Math.Acos(Math.Max(-1,Math.Min(1,u*v)));}
        double th=0.06;bool[] st=new bool[n];for(int i=0;i<n;i++)st[i]=turn[i]<th;
        int start=-1;for(int i=0;i<n;i++)if(!st[i]){start=i;break;} if(start<0)start=0;
        var runs=new List<int[]>(); var runLen=new List<double>(); int idx=start,counted=0;
        while(counted<n){int g=idx%n;if(st[g]){int a=g;double len=0;int prev=g;while(counted<n&&st[idx%n]){int cur=idx%n;if(cur!=a){len+=loop[cur].DistanceTo(loop[prev]);prev=cur;}idx++;counted++;}int b=(idx-1)%n;runs.Add(new[]{a,b});runLen.Add(len);}else{idx++;counted++;}}
        var order=Enumerable.Range(0,runs.Count).OrderByDescending(i=>runLen[i]).Take(2).ToList();
        var cs=new List<int>(); foreach(var oi in order){cs.Add(runs[oi][0]);cs.Add(runs[oi][1]);}
        cs=cs.Distinct().OrderBy(v=>v).ToList(); while(cs.Count<4)cs.Add((cs.Count*n)/4); while(cs.Count>4)cs.RemoveAt(cs.Count-1);
        corners=cs.ToArray(); live=new bool[4]; var sA=new double[4];
        for(int e=0;e<4;e++){int a=corners[e],b=corners[(e+1)%4];var seg=new List<Point3d>();int j=a;while(true){seg.Add(loop[j]);if(j==b)break;j=(j+1)%n;}double arc=0;for(int k=1;k<seg.Count;k++)arc+=seg[k].DistanceTo(seg[k-1]);double ch=seg[0].DistanceTo(seg[seg.Count-1]);sA[e]=arc>1e-9?ch/arc:1.0;}
        var ord=Enumerable.Range(0,4).OrderByDescending(e=>sA[e]).ToList(); for(int e=0;e<4;e++)live[e]=!(e==ord[0]||e==ord[1]);
    }

    // ---- orient (sawn ends vertical) and sample the two live edges as y = f(local x) ----
    static bool Extract(Point3d[] raw, double dx, out double[] bY, out double[] tY, out double width){
        bY=null;tY=null;width=0;
        var loop=Resample(raw,160); int[] corners; bool[] lf; Classify(loop,out corners,out lf); int n=loop.Length;
        var sawnE=Enumerable.Range(0,4).Where(e=>!lf[e]).ToList(); if(sawnE.Count!=2) return false;
        Func<int,List<Point3d>> EP=(e)=>{int a=corners[e],b=corners[(e+1)%4];var seg=new List<Point3d>();int j=a;while(true){seg.Add(loop[j]);if(j==b)break;j=(j+1)%n;}return seg;};
        Func<int,Point3d> Mid=(e)=>{var s2=EP(e);return s2[0]+(s2[s2.Count-1]-s2[0])*0.5;};
        Vector3d axis=Mid(sawnE[1])-Mid(sawnE[0]); if(axis.Length<1e-6)return false; axis.Unitize();
        double ang=-Math.Atan2(axis.Y,axis.X),ca=Math.Cos(ang),sa=Math.Sin(ang);
        Func<Point3d,Point3d> Rot=(p)=>new Point3d(p.X*ca-p.Y*sa,p.X*sa+p.Y*ca,0);
        var Rr=loop.Select(Rot).ToArray();
        Func<int,List<Point3d>> RE=(e)=>{int a=corners[e],b=corners[(e+1)%4];var seg=new List<Point3d>();int j=a;while(true){seg.Add(Rr[j]);if(j==b)break;j=(j+1)%n;}return seg;};
        var liveE=Enumerable.Range(0,4).Where(e=>lf[e]).ToList(); if(liveE.Count!=2)return false;
        var L0=RE(liveE[0]);var L1=RE(liveE[1]); double my0=L0.Average(p=>p.Y),my1=L1.Average(p=>p.Y);
        var botE=my0<my1?L0:L1; var topE=my0<my1?L1:L0;
        double minx=Rr.Min(p=>p.X);
        botE=botE.Select(p=>new Point3d(p.X-minx,p.Y,0)).ToList(); topE=topE.Select(p=>new Point3d(p.X-minx,p.Y,0)).ToList();
        width=Rr.Max(p=>p.X)-minx; int Ln=Math.Max(2,(int)Math.Round(width/dx)+1);
        Func<List<Point3d>,double,double> YAt=(pl,x)=>{var sp=pl.OrderBy(p=>p.X).ToList();if(x<=sp[0].X)return sp[0].Y;if(x>=sp[sp.Count-1].X)return sp[sp.Count-1].Y;for(int i=1;i<sp.Count;i++)if(sp[i].X>=x){double t=(x-sp[i-1].X)/Math.Max(1e-9,sp[i].X-sp[i-1].X);return sp[i-1].Y+(sp[i].Y-sp[i-1].Y)*t;}return sp[sp.Count-1].Y;};
        bY=new double[Ln];tY=new double[Ln];
        for(int i=0;i<Ln;i++){double x=Math.Min(width,i*dx);bY[i]=YAt(botE,x);tY[i]=YAt(topE,x);}
        return true;
    }

    // ---- match + scribe into predefined smooth rivers; brick-bond staggered layup ----
    public static List<Mesh> BuildFloor(int seed){
        _s=(ulong)seed; double dx=2.0;
        var boards=new List<double[][]>(); var widths=new List<double>();
        for(int i=0;i<80;i++){double[] bY,tY;double width;if(Extract(MakeBoard(),dx,out bY,out tY,out width)){boards.Add(new[]{bY,tY});widths.Add(width);}}
        double FW=520, Hc=60; int R=5; int Nx=(int)(FW/dx)+1;
        Func<double,int> gi=(x)=>Math.Max(0,Math.Min(Nx-1,(int)Math.Round(x/dx)));
        var river=new double[R+1][];
        for(int r=0;r<=R;r++){double p1=Rnd()*6.28,p2=Rnd()*6.28;double w1=2*Math.PI/(150+Rnd()*80),w2=2*Math.PI/(80+Rnd()*40);double a2=0.45+Rnd()*0.2;river[r]=new double[Nx];for(int i=0;i<Nx;i++)river[r][i]=r*Hc+5.0*(Math.Sin(w1*i*dx+p1)+a2*Math.Sin(w2*i*dx+p2));}
        int[][] PAL=new int[][]{ new[]{201,166,107},new[]{185,140,82},new[]{216,190,142},new[]{169,116,63},new[]{193,154,91},new[]{227,207,163},new[]{181,133,74},new[]{205,166,112},new[]{156,107,58},new[]{221,197,150},new[]{203,176,121},new[]{191,160,106} };
        var used=new bool[boards.Count]; var meshes=new List<Mesh>(); var prevJoints=new List<double>(); int placed=0;
        for(int r=0;r<R;r++){
            double cursor=0; var joints=new List<double>(); int guard=0;
            while(cursor<FW-12 && guard++<60){
                int best=-1;double bestScore=1e18,bestDy=0;int bestLe=0;
                for(int b=0;b<boards.Count;b++){if(used[b])continue;double[] bY=boards[b][0],tY=boards[b][1];double width=widths[b];int Ln=bY.Length;
                    int Le=Ln;for(int i=0;i<Ln;i++){if(cursor+i*dx>FW){Le=i;break;}} if(Le<2)continue;
                    double sum=0;for(int i=0;i<Le;i++){double gx=cursor+i*dx;sum+=river[r][gi(gx)]-bY[i];}double dyc=sum/Le;
                    double tb=0,tt=0;for(int i=0;i<Le;i++){double gx=cursor+i*dx;tb+=Math.Abs((bY[i]+dyc)-river[r][gi(gx)]);tt+=Math.Abs((tY[i]+dyc)-river[r+1][gi(gx)]);}tb/=Le;tt/=Le;
                    double jx=cursor+width;double pen=0;foreach(var pj in prevJoints){double d=Math.Abs(jx-pj);if(d<24)pen+=(24-d)*0.5;}
                    double score=tb+tt+pen;
                    if(score<bestScore){bestScore=score;best=b;bestDy=dyc;bestLe=Le;}
                }
                if(best<0)break;
                used[best]=true;double W=widths[best];int Le2=bestLe;
                var m=new Mesh();
                for(int i=0;i<Le2;i++){double gx=cursor+i*dx;m.Vertices.Add(new Point3d(gx,river[r][gi(gx)],0));}
                for(int i=0;i<Le2;i++){double gx=cursor+i*dx;m.Vertices.Add(new Point3d(gx,river[r+1][gi(gx)],0));}
                for(int i=0;i<Le2-1;i++)m.Faces.AddFace(i,i+1,Le2+i+1,Le2+i);
                int[] c=PAL[(r*7+placed)%PAL.Length]; var col=Color.FromArgb(c[0],c[1],c[2]);
                m.VertexColors.Clear(); for(int i=0;i<m.Vertices.Count;i++)m.VertexColors.Add(col);
                m.Normals.ComputeNormals();
                meshes.Add(m);
                joints.Add(cursor+W); cursor+=W; placed++;
            }
            prevJoints=joints;
        }
        return meshes;
    }
}
