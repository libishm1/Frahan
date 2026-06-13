/* ============================================================================
 * nfp_kernel.cpp — batched No-Fit-Polygon kernel over vendored Clipper2 C++.
 *
 * See nfp_kernel_capi.h for the exported contract. Implementation notes:
 *
 *  - All input/output coordinates are doubles in the caller's space; every
 *    boolean runs on Clipper2's exact Int64 lane after multiplying by `scale`
 *    (llround snap). scale = 100.0 mirrors the managed Clipper2 PathD lane at
 *    decimal precision 2, which is what the managed nester uses.
 *  - The RDP simplifier is a verbatim port of the managed nester's
 *    SimplifyLoop/RdpMark (closed-loop variant, anchors at 0 and n/2, loops
 *    of <= 8 vertices untouched) so the native lane prunes the same way.
 *  - Angles are independent work items: they may be computed on a small
 *    thread pool, but results are assembled strictly in (angleIdx, obstIdx,
 *    clipper-output) order and each item's arithmetic is self-contained, so
 *    output is deterministic regardless of scheduling.
 *  - No allocation crosses the C boundary: caller-provided buffers only.
 *
 * Clipper2 is Angus Johnson's polygon clipping library (BSL-1.0), vendored
 * unmodified under ./clipper2/ at tag Clipper2_2.0.1.
 * ========================================================================== */

#define NFP_KERNEL_BUILD
#include "nfp_kernel_capi.h"

#include "clipper2/clipper.engine.h"
#include "clipper2/clipper.minkowski.h"

#include <algorithm>
#include <atomic>
#include <climits>
#include <cmath>
#include <cstdint>
#include <thread>
#include <vector>

using namespace Clipper2Lib;

namespace {

constexpr double kEps = 1e-6; // matches the managed nester's Eps

// ── RDP, closed-loop variant — verbatim port of the managed RdpMark ───────
void RdpMark(const PathD& p, size_t a, size_t b, double tol, std::vector<char>& keep)
{
    if (b - a < 2) return;
    const PointD& pa = p[a];
    const PointD& pb = p[b % p.size()];
    const double dx = pb.x - pa.x, dy = pb.y - pa.y;
    const double len = std::sqrt(dx * dx + dy * dy);
    size_t worst = SIZE_MAX;
    double worstD = tol;
    for (size_t i = a + 1; i < b; ++i)
    {
        double d;
        if (len < kEps)
        {
            const double ex = p[i].x - pa.x, ey = p[i].y - pa.y;
            d = std::sqrt(ex * ex + ey * ey);
        }
        else
        {
            d = std::fabs(dx * (pa.y - p[i].y) - (pa.x - p[i].x) * dy) / len;
        }
        if (d > worstD) { worstD = d; worst = i; }
    }
    if (worst == SIZE_MAX) return;
    keep[worst] = 1;
    RdpMark(p, a, worst, tol, keep);
    RdpMark(p, worst, b, tol, keep);
}

PathD SimplifyLoop(const PathD& p, double tol)
{
    if (p.size() <= 8 || tol <= 0) return p;
    std::vector<char> keep(p.size(), 0);
    keep[0] = 1;
    keep[p.size() / 2] = 1;
    RdpMark(p, 0, p.size() / 2, tol, keep);
    RdpMark(p, p.size() / 2, p.size(), tol, keep); // wraps to index 0
    PathD r;
    r.reserve(p.size());
    for (size_t i = 0; i < p.size(); ++i)
        if (keep[i]) r.push_back(p[i]);
    return r.size() >= 3 ? r : p;
}

double BBoxDiag(const PathD& p)
{
    double mnx = 1e308, mny = 1e308, mxx = -1e308, mxy = -1e308;
    for (const PointD& v : p)
    {
        if (v.x < mnx) mnx = v.x;
        if (v.y < mny) mny = v.y;
        if (v.x > mxx) mxx = v.x;
        if (v.y > mxy) mxy = v.y;
    }
    const double dx = mxx - mnx, dy = mxy - mny;
    return std::sqrt(dx * dx + dy * dy);
}

// simplifyTol > 0: absolute; < 0: relative to the shape's bbox diagonal; 0: off
double EffTol(const PathD& p, double simplifyTol)
{
    if (simplifyTol > 0) return simplifyTol;
    if (simplifyTol < 0) return -simplifyTol * BBoxDiag(p);
    return 0.0;
}

Path64 ToPath64(const PathD& p, double scale)
{
    Path64 r;
    r.reserve(p.size());
    for (const PointD& v : p)
        r.emplace_back(static_cast<int64_t>(std::llround(v.x * scale)),
                       static_cast<int64_t>(std::llround(v.y * scale)));
    return r;
}

} // namespace

extern "C" NFP_API int nfp_batch(
    const double* partXY, int partVerts,
    const double* anglesRad, int angleCount,
    const double* obstXY, const int* obstVerts, int obstCount,
    double scale, double simplifyTol,
    double* outXY, int outCapacityDoubles,
    int* outLoopVerts, int* outLoopAngleIdx, int* outLoopObstIdx,
    int loopCapacity, int* outLoopCount)
{
    if (!outLoopCount) return NFP_ERR_BAD_ARGS;
    *outLoopCount = 0;
    if (!partXY || partVerts < 3 || !anglesRad || angleCount < 1) return NFP_ERR_BAD_ARGS;
    if (obstCount < 0) return NFP_ERR_BAD_ARGS;
    if (obstCount > 0 && (!obstXY || !obstVerts)) return NFP_ERR_BAD_ARGS;
    if (!outXY || !outLoopVerts || !outLoopAngleIdx || !outLoopObstIdx) return NFP_ERR_BAD_ARGS;
    if (outCapacityDoubles < 0 || loopCapacity < 0) return NFP_ERR_BAD_ARGS;
    if (!(scale > 0.0) || !std::isfinite(scale)) return NFP_ERR_BAD_ARGS;

    // finiteness gate (review finding): NaN/Inf coordinates reach llround UB
    // and can wedge the boolean engine for minutes; reject loudly instead
    {
        const long long pn = 2LL * partVerts;
        for (long long i = 0; i < pn; ++i)
            if (!std::isfinite(partXY[i])) return NFP_ERR_BAD_ARGS;
        for (int a = 0; a < angleCount; ++a)
            if (!std::isfinite(anglesRad[a])) return NFP_ERR_BAD_ARGS;
        long long total = 0;
        for (int oi = 0; oi < obstCount; ++oi)
        {
            if (obstVerts[oi] < 0) return NFP_ERR_BAD_ARGS;
            total += obstVerts[oi];
        }
        const long long on = 2LL * total;
        for (long long i = 0; i < on; ++i)
            if (!std::isfinite(obstXY[i])) return NFP_ERR_BAD_ARGS;
    }

    try
    {
        // ── obstacles: read, simplify, snap to Int64 ONCE (angle-invariant) ──
        std::vector<Path64> obst64(static_cast<size_t>(obstCount));
        {
            const double* cur = obstXY;
            for (int oi = 0; oi < obstCount; ++oi)
            {
                const int n = obstVerts[oi];
                if (n < 0) return NFP_ERR_BAD_ARGS;
                if (n >= 3)
                {
                    PathD o;
                    o.reserve(static_cast<size_t>(n));
                    for (int k = 0; k < n; ++k) o.emplace_back(cur[2 * k], cur[2 * k + 1]);
                    obst64[static_cast<size_t>(oi)] =
                        ToPath64(SimplifyLoop(o, EffTol(o, simplifyTol)), scale);
                }
                cur += 2 * static_cast<long long>(n);
                // n < 3: degenerate, contributes no loops
            }
        }

        PathD part;
        part.reserve(static_cast<size_t>(partVerts));
        for (int k = 0; k < partVerts; ++k) part.emplace_back(partXY[2 * k], partXY[2 * k + 1]);

        // ── per-angle NFPs; deterministic slot assembly ──────────────────────
        std::vector<std::vector<Paths64>> results(static_cast<size_t>(angleCount));
        std::atomic<int> failed(0);

        auto worker = [&](int ai) noexcept
        {
            try
            {
                const double c = std::cos(anglesRad[ai]), s = std::sin(anglesRad[ai]);
                PathD refl;
                refl.reserve(part.size());
                for (const PointD& v : part)
                {
                    const double rx = v.x * c - v.y * s;
                    const double ry = v.x * s + v.y * c;
                    refl.emplace_back(-rx, -ry); // reflect(rotate(part, a))
                }
                const Path64 refl64 =
                    ToPath64(SimplifyLoop(refl, EffTol(refl, simplifyTol)), scale);
                auto& slot = results[static_cast<size_t>(ai)];
                slot.resize(static_cast<size_t>(obstCount));
                if (refl64.size() < 3) return;
                for (int oi = 0; oi < obstCount; ++oi)
                {
                    const Path64& o64 = obst64[static_cast<size_t>(oi)];
                    if (o64.size() < 3) continue;
                    // NFP(obstacle, part@a) = MinkowskiSum(obstacle, refl),
                    // NonZero-unioned inside Clipper2's MinkowskiSum.
                    slot[static_cast<size_t>(oi)] = MinkowskiSum(o64, refl64, true);
                }
            }
            catch (...) { failed.store(1, std::memory_order_relaxed); }
        };

        unsigned hw = std::thread::hardware_concurrency();
        int nThreads = static_cast<int>(std::min<unsigned>(hw ? hw : 1u,
                                        static_cast<unsigned>(angleCount)));
        if (nThreads > 16) nThreads = 16;
        // thread-spawn costs more than it saves on tiny batches: Minkowski
        // work ~ angleCount x sum(|obst_i| x |part|) swept quads. Measured on
        // the 7-shield bench (48-vert parts): batches of ~27k quads still gain
        // ~2x from the pool, so only truly tiny batches (4-vert rect lanes,
        // ~3k quads) run single-thread. Results are identical either way.
        {
            long long quadWork = 0;
            for (const Path64& o : obst64)
                quadWork += static_cast<long long>(o.size()) * static_cast<long long>(partVerts);
            quadWork *= angleCount;
            if (quadWork < 4000) nThreads = 1;
        }
        if (nThreads <= 1)
        {
            for (int ai = 0; ai < angleCount; ++ai) worker(ai);
        }
        else
        {
            std::atomic<int> next(0);
            std::vector<std::thread> pool;
            pool.reserve(static_cast<size_t>(nThreads));
            for (int t = 0; t < nThreads; ++t)
                pool.emplace_back([&]() noexcept
                {
                    for (;;)
                    {
                        const int ai = next.fetch_add(1, std::memory_order_relaxed);
                        if (ai >= angleCount) return;
                        worker(ai);
                    }
                });
            for (std::thread& th : pool) th.join();
        }
        if (failed.load(std::memory_order_relaxed)) return NFP_ERR_EXCEPTION;

        // ── capacity check, then flat write-out in deterministic order ──────
        long long needLoops = 0, needDoubles = 0;
        for (const auto& slot : results)
            for (const Paths64& nfp : slot)
                for (const Path64& loop : nfp)
                {
                    ++needLoops;
                    needDoubles += 2 * static_cast<long long>(loop.size());
                }
        if (needLoops > loopCapacity || needDoubles > outCapacityDoubles)
        {
            *outLoopCount = static_cast<int>(std::min<long long>(needLoops, INT_MAX));
            if (loopCapacity >= 1)
                outLoopVerts[0] = static_cast<int>(std::min<long long>(needDoubles, INT_MAX));
            return NFP_ERR_CAPACITY;
        }

        const double inv = 1.0 / scale;
        double* px = outXY;
        int li = 0;
        for (int ai = 0; ai < angleCount; ++ai)
        {
            const auto& slot = results[static_cast<size_t>(ai)];
            for (int oi = 0; oi < static_cast<int>(slot.size()); ++oi)
                for (const Path64& loop : slot[static_cast<size_t>(oi)])
                {
                    outLoopVerts[li] = static_cast<int>(loop.size());
                    outLoopAngleIdx[li] = ai;
                    outLoopObstIdx[li] = oi;
                    ++li;
                    for (const Point64& pt : loop)
                    {
                        *px++ = static_cast<double>(pt.x) * inv;
                        *px++ = static_cast<double>(pt.y) * inv;
                    }
                }
        }
        *outLoopCount = li;
        return NFP_OK;
    }
    catch (...)
    {
        return NFP_ERR_EXCEPTION;
    }
}
