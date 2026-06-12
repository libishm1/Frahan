/* test_nfp_kernel.c — ctypes-free smoke test for nfp_kernel.dll.
 *
 * Hand-checked cases:
 *  1. unit square part vs unit square obstacle, angle 0:
 *     NFP = MinkowskiSum(square, -square) = [-1,1]^2 -> 1 loop, area 4.
 *  2. 2x1 rect part vs unit square obstacle, angles {0, pi/2}:
 *     both NFPs have area 6; angle-0 bbox = [-2,1]x[-1,1].
 *  3. capacity contract: undersized buffers -> rc 1 + required sizes,
 *     retry with the reported sizes succeeds.
 *
 * Build (llvm-mingw):
 *   x86_64-w64-mingw32-gcc -O2 -o test_nfp_kernel.exe test_nfp_kernel.c
 * Run beside nfp_kernel.dll. Exit code 0 = pass.
 */
#include <windows.h>
#include <stdio.h>
#include <math.h>

typedef int (*nfp_batch_fn)(
    const double*, int, const double*, int,
    const double*, const int*, int,
    double, double,
    double*, int, int*, int*, int*, int, int*);

static double LoopArea(const double* xy, int n)
{
    double a = 0.0;
    for (int i = 0; i < n; ++i)
    {
        const double* u = xy + 2 * i;
        const double* v = xy + 2 * ((i + 1) % n);
        a += u[0] * v[1] - v[0] * u[1];
    }
    return fabs(a / 2.0);
}

static int g_fail = 0;
#define CHECK(cond, msg) do { \
    if (!(cond)) { printf("FAIL: %s\n", msg); g_fail = 1; } \
    else printf("ok:   %s\n", msg); } while (0)

int main(void)
{
    HMODULE h = LoadLibraryA("nfp_kernel.dll");
    if (!h) { printf("FAIL: LoadLibrary(nfp_kernel.dll) -> %lu\n", GetLastError()); return 1; }
    nfp_batch_fn nfp_batch = (nfp_batch_fn)GetProcAddress(h, "nfp_batch");
    if (!nfp_batch) { printf("FAIL: GetProcAddress(nfp_batch)\n"); return 1; }

    double outXY[4096];
    int loopVerts[64], loopAngle[64], loopObst[64], loopCount = 0;

    /* ── case 1: unit square vs unit square, angle 0 ───────────────────── */
    {
        double part[] = { 0,0, 1,0, 1,1, 0,1 };
        double angles[] = { 0.0 };
        double obst[] = { 0,0, 1,0, 1,1, 0,1 };
        int obstVerts[] = { 4 };
        int rc = nfp_batch(part, 4, angles, 1, obst, obstVerts, 1, 100.0, 0.0,
                           outXY, 4096, loopVerts, loopAngle, loopObst, 64, &loopCount);
        CHECK(rc == 0, "case1 rc == 0");
        CHECK(loopCount == 1, "case1 one result loop");
        CHECK(loopAngle[0] == 0 && loopObst[0] == 0, "case1 (angle,obst) tag = (0,0)");
        double area = LoopArea(outXY, loopVerts[0]);
        CHECK(fabs(area - 4.0) < 1e-9, "case1 NFP area == 4 (hand-checked [-1,1]^2)");
        double mnx = 1e308, mny = 1e308, mxx = -1e308, mxy = -1e308;
        for (int i = 0; i < loopVerts[0]; ++i)
        {
            if (outXY[2*i]   < mnx) mnx = outXY[2*i];
            if (outXY[2*i]   > mxx) mxx = outXY[2*i];
            if (outXY[2*i+1] < mny) mny = outXY[2*i+1];
            if (outXY[2*i+1] > mxy) mxy = outXY[2*i+1];
        }
        CHECK(fabs(mnx + 1) < 1e-9 && fabs(mny + 1) < 1e-9 &&
              fabs(mxx - 1) < 1e-9 && fabs(mxy - 1) < 1e-9,
              "case1 NFP bbox == [-1,1]x[-1,1]");
    }

    /* ── case 2: 2x1 rect part, angles {0, pi/2}, unit square obstacle ─── */
    {
        double part[] = { 0,0, 2,0, 2,1, 0,1 };
        double angles[] = { 0.0, 1.5707963267948966 };
        double obst[] = { 0,0, 1,0, 1,1, 0,1 };
        int obstVerts[] = { 4 };
        int rc = nfp_batch(part, 4, angles, 2, obst, obstVerts, 1, 100.0, 0.0,
                           outXY, 4096, loopVerts, loopAngle, loopObst, 64, &loopCount);
        CHECK(rc == 0, "case2 rc == 0");
        CHECK(loopCount == 2, "case2 two result loops (one per angle)");
        CHECK(loopAngle[0] == 0 && loopAngle[1] == 1, "case2 loops ordered by angle");
        double a0 = LoopArea(outXY, loopVerts[0]);
        double a1 = LoopArea(outXY + 2 * loopVerts[0], loopVerts[1]);
        CHECK(fabs(a0 - 6.0) < 1e-6, "case2 angle-0 NFP area == 6");
        CHECK(fabs(a1 - 6.0) < 1e-6, "case2 angle-90 NFP area == 6");
        double mnx = 1e308, mny = 1e308, mxx = -1e308, mxy = -1e308;
        for (int i = 0; i < loopVerts[0]; ++i)
        {
            if (outXY[2*i]   < mnx) mnx = outXY[2*i];
            if (outXY[2*i]   > mxx) mxx = outXY[2*i];
            if (outXY[2*i+1] < mny) mny = outXY[2*i+1];
            if (outXY[2*i+1] > mxy) mxy = outXY[2*i+1];
        }
        CHECK(fabs(mnx + 2) < 1e-9 && fabs(mny + 1) < 1e-9 &&
              fabs(mxx - 1) < 1e-9 && fabs(mxy - 1) < 1e-9,
              "case2 angle-0 bbox == [-2,1]x[-1,1]");
    }

    /* ── case 3: capacity contract ─────────────────────────────────────── */
    {
        double part[] = { 0,0, 1,0, 1,1, 0,1 };
        double angles[] = { 0.0 };
        double obst[] = { 0,0, 1,0, 1,1, 0,1 };
        int obstVerts[] = { 4 };
        double tinyXY[4];
        int tinyVerts[1], tinyAngle[1], tinyObst[1];
        int rc = nfp_batch(part, 4, angles, 1, obst, obstVerts, 1, 100.0, 0.0,
                           tinyXY, 4, tinyVerts, tinyAngle, tinyObst, 1, &loopCount);
        CHECK(rc == 1, "case3 undersized buffers -> rc 1");
        CHECK(loopCount == 1, "case3 required loop count reported");
        int needDoubles = tinyVerts[0];
        CHECK(needDoubles >= 8, "case3 required double count reported (>= 8)");
        int rc2 = nfp_batch(part, 4, angles, 1, obst, obstVerts, 1, 100.0, 0.0,
                            outXY, needDoubles, loopVerts, loopAngle, loopObst,
                            loopCount, &loopCount);
        CHECK(rc2 == 0, "case3 retry with reported sizes -> rc 0");
        /* required doubles must equal what the successful run wrote */
        CHECK(needDoubles == 2 * loopVerts[0], "case3 reported size matches written loop");
    }

    /* ── determinism: same input twice -> identical bytes ──────────────── */
    {
        double part[] = { 0,0, 2,0, 2,1, 0,1 };
        double angles[] = { 0.0, 0.7853981633974483, 1.5707963267948966 };
        double obst[] = { 0,0, 1,0, 1,1, 0,1,  3,3, 5,3, 5,6, 3,6 };
        int obstVerts[] = { 4, 4 };
        double xyA[4096], xyB[4096];
        int vA[64], aA[64], oA[64], cA = 0, vB[64], aB[64], oB[64], cB = 0;
        int r1 = nfp_batch(part, 4, angles, 3, obst, obstVerts, 2, 100.0, -2e-3,
                           xyA, 4096, vA, aA, oA, 64, &cA);
        int r2 = nfp_batch(part, 4, angles, 3, obst, obstVerts, 2, 100.0, -2e-3,
                           xyB, 4096, vB, aB, oB, 64, &cB);
        int same = (r1 == 0 && r2 == 0 && cA == cB);
        int nd = 0;
        for (int i = 0; same && i < cA; ++i)
        {
            if (vA[i] != vB[i] || aA[i] != aB[i] || oA[i] != oB[i]) same = 0;
            nd += 2 * vA[i];
        }
        for (int i = 0; same && i < nd; ++i) if (xyA[i] != xyB[i]) same = 0;
        CHECK(same, "determinism: two identical batched calls byte-match");
    }

    printf(g_fail ? "SMOKE TEST FAILED\n" : "SMOKE TEST PASSED\n");
    return g_fail;
}
