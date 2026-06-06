#nullable disable
using System;

namespace Frahan.Kintsugi.Port.Primitives;

/// <summary>
/// PuzzleFusion++ custom diffusion scheduler. Port of
/// `puzzlefusion_plusplus/denoiser/model/modules/custom_diffusers.py`.
///
/// PIECEWISE-QUADRATIC alpha_bar(t) schedule (NOT linear DDPM):
///   t_norm = t / num_train_steps           # in [0, 1]
///   if t_norm <= 0.7:
///     alpha_bar(t) = 1 - 0.1 * (t_norm / 0.7)^2
///   else:
///     alpha_bar(t) = 0.9 * (1 - ((t_norm - 0.7) / 0.3)^2)
///
/// beta_t = 1 - alpha_bar(t) / alpha_bar(t-1)  (clipped to [0, 1])
///
/// INFERENCE
///   set_timesteps(20) -> 20 timesteps in [0, T) descending.
///   For each t in timesteps (high -> low):
///     pred_noise = denoiser(x_t, t, latent, ...)
///     x_{t-1} = scheduler.step(pred_noise, x_t, t)
///
/// We implement DDPM step (NOT DDIM): noise-prediction parameterisation,
/// no additional Gaussian noise at step t=0.
/// </summary>
public sealed class DiffusionScheduler
{
    public int NumTrainSteps { get; }
    public int NumInferenceSteps { get; private set; }
    public float[] AlphaBars { get; }   // [NumTrainSteps]
    public float[] Betas { get; }       // [NumTrainSteps]
    public float[] Alphas { get; }      // 1 - betas
    public int[] InferenceTimesteps { get; private set; }

    public DiffusionScheduler(int numTrainSteps = 1000)
    {
        NumTrainSteps = numTrainSteps;
        AlphaBars = new float[numTrainSteps];
        Betas = new float[numTrainSteps];
        Alphas = new float[numTrainSteps];
        for (int t = 0; t < numTrainSteps; t++)
        {
            float tNorm = (float)t / (numTrainSteps - 1);
            AlphaBars[t] = AlphaBar(tNorm);
        }
        for (int t = 0; t < numTrainSteps; t++)
        {
            float aPrev = t == 0 ? 1.0f : AlphaBars[t - 1];
            float a = AlphaBars[t];
            float beta = 1.0f - (a / Math.Max(1e-8f, aPrev));
            beta = Math.Max(0f, Math.Min(0.999f, beta));
            Betas[t] = beta;
            Alphas[t] = 1.0f - beta;
        }
    }

    private static float AlphaBar(float tNorm)
    {
        if (tNorm <= 0.7f)
        {
            float u = tNorm / 0.7f;
            return 1.0f - 0.1f * u * u;
        }
        else
        {
            float u = (tNorm - 0.7f) / 0.3f;
            return 0.9f * (1.0f - u * u);
        }
    }

    /// <summary>Configure inference for N steps. Sets up the descending
    /// timestep schedule (matches diffusers' 'leading' timestep spacing).</summary>
    public void SetTimesteps(int numInferenceSteps)
    {
        NumInferenceSteps = numInferenceSteps;
        int step = NumTrainSteps / numInferenceSteps;
        InferenceTimesteps = new int[numInferenceSteps];
        for (int i = 0; i < numInferenceSteps; i++)
            InferenceTimesteps[i] = (numInferenceSteps - 1 - i) * step;
    }

    /// <summary>
    /// One DDPM step: predict x_{t-1} from x_t and predicted noise.
    /// Matches diffusers' DDPMScheduler.step with
    /// PREDICT_TYPE='epsilon', clipping disabled, no added noise at t=0.
    /// </summary>
    /// <param name="modelOutput">Predicted noise epsilon [N].</param>
    /// <param name="xT">Current x_t [N].</param>
    /// <param name="t">Current timestep index in [0, NumTrainSteps).</param>
    /// <returns>x_{t-1} [N].</returns>
    public float[] Step(float[] modelOutput, float[] xT, int t)
    {
        if (modelOutput.Length != xT.Length)
            throw new ArgumentException("modelOutput and xT length mismatch");
        float aBar = AlphaBars[t];
        float sqrtAbar = (float)Math.Sqrt(Math.Max(1e-8f, aBar));
        float sqrtOneMinusAbar = (float)Math.Sqrt(Math.Max(0f, 1.0f - aBar));

        // Predict x_0 from x_t and epsilon (epsilon-parameterisation):
        //   x_0 = (x_t - sqrt(1 - aBar) * eps) / sqrt(aBar)
        var x0Pred = new float[xT.Length];
        for (int i = 0; i < xT.Length; i++)
            x0Pred[i] = (xT[i] - sqrtOneMinusAbar * modelOutput[i]) / sqrtAbar;

        // At t == 0 (the LAST inference step), we want the predicted clean
        // x_0 directly. The posterior-mean computation would divide by
        // (1 - aBar) which goes to 0 -- producing a degenerate zero pose.
        // Matches diffusers' DDPMScheduler.step branch when t == 0.
        if (t == 0) return x0Pred;

        int prev = Math.Max(0, FindPreviousTimestep(t));
        float aBarPrev = prev >= 0 ? AlphaBars[prev] : 1.0f;
        float beta = 1.0f - aBar / Math.Max(1e-8f, aBarPrev);
        beta = Math.Max(0f, Math.Min(0.999f, beta));
        float alpha = 1.0f - beta;
        float sqrtAbarPrev = (float)Math.Sqrt(Math.Max(0f, aBarPrev));
        float sqrtAlpha = (float)Math.Sqrt(Math.Max(0f, alpha));
        // Guard the (1 - aBar) denominator -- only safe to compute the
        // posterior mean when (1 - aBar) is comfortably positive.
        float oneMinusAbar = 1.0f - aBar;
        if (oneMinusAbar < 1e-6f) return x0Pred;
        float coefX0 = sqrtAbarPrev * beta / oneMinusAbar;
        float coefXt = sqrtAlpha * (1.0f - aBarPrev) / oneMinusAbar;

        var result = new float[xT.Length];
        for (int i = 0; i < xT.Length; i++)
            result[i] = coefX0 * x0Pred[i] + coefXt * xT[i];
        return result;
    }

    private int FindPreviousTimestep(int t)
    {
        if (InferenceTimesteps == null) return t - 1;
        for (int i = 0; i < InferenceTimesteps.Length; i++)
            if (InferenceTimesteps[i] == t && i + 1 < InferenceTimesteps.Length)
                return InferenceTimesteps[i + 1];
        return Math.Max(0, t - 1);
    }
}
