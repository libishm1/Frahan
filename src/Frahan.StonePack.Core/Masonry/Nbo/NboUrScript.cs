#nullable disable
using System;
using System.Globalization;
using System.Text;

namespace Frahan.Masonry.Nbo;

// =============================================================================
// NboUrScript -- emit Universal-Robots URScript to PLACE and FORCE-SEAT a stone.
// TEXT ONLY: this is code generation, never a socket send -- hardware-in-loop
// stays dormant (AGENTS.md truth criterion). The robot backend (Robots/visose
// "Remote UR", UnderAutomation, or Dashboard load+play) is what actually streams
// the program.
//
// Force-seating is the irregular-stone enabler: a flat-bottomed stone resting on
// a bumpy course touches at high points (the wedge voids the analytic gate sees);
// pressing it down with force_mode lets it rock into seat -- the physical analog
// of the planner's drop-to-contact + settle. Furrer 2017 (the NBO precedent) ran
// exactly this on a UR10 + FT150 force-torque sensor.
//
//   movej(approach) -> movel(down to seat) -> zero_ftsensor()
//   -> force_mode(seat_frame, select Z, wrench Fz, type 2, limits) -> dwell
//   -> end_force_mode() -> movel(retract to approach)
//
// Poses are UR p[x,y,z, rx,ry,rz] (metres + axis-angle), from NboGrasp.ToUrPose.
// =============================================================================

/// <summary>Parameters for a force-seated place program.</summary>
public sealed class UrSeatOptions
{
    /// <summary>Free-move speed / accel (m/s, m/s^2).</summary>
    public double MoveSpeed { get; set; } = 0.25;
    public double MoveAccel { get; set; } = 0.6;
    /// <summary>Compliant descent speed during seating (m/s).</summary>
    public double DescendSpeed { get; set; } = 0.04;
    /// <summary>Downward press force along the seat-frame Z, in newtons.</summary>
    public double SeatForce { get; set; } = 50.0;
    /// <summary>Seconds to hold the press before releasing.</summary>
    public double SeatDwell { get; set; } = 1.5;
    /// <summary>force_mode lateral deviation limit (m) on the stiff axes.</summary>
    public double MaxLateralDev { get; set; } = 0.01;
    /// <summary>URScript function name.</summary>
    public string FunctionName { get; set; } = "frahan_place";
}

public static class NboUrScript
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    /// <summary>
    /// A full place-and-seat URScript program for one stone: approach above,
    /// descend to the seat frame, zero the F/T sensor, force_mode press down the
    /// seat-frame Z until the stone settles, then retract.
    /// </summary>
    public static string PlaceAndSeat(RobotPose seat, RobotPose approach, UrSeatOptions opt = null)
    {
        opt = opt ?? new UrSeatOptions();
        var sb = new StringBuilder();
        sb.AppendLine("def " + Ident(opt.FunctionName) + "():");
        sb.AppendLine("  # Frahan NBO place + force-seat. GENERATED -- validate in URSim before any hardware.");
        sb.AppendLine("  textmsg(\"frahan: approach\")");
        sb.AppendLine("  movej(" + Pose(approach) + ", a=" + N(opt.MoveAccel) + ", v=" + N(opt.MoveSpeed) + ")");
        sb.AppendLine("  movel(" + Pose(seat) + ", a=" + N(opt.MoveAccel) + ", v=" + N(opt.DescendSpeed) + ")");
        sb.AppendLine("  zero_ftsensor()");
        sb.AppendLine("  # press along the seat-frame Z (into the bed) so the stone rocks into seat");
        sb.Append("  ").AppendLine(SeatBlock(seat, opt));
        sb.AppendLine("  sleep(" + N(opt.SeatDwell) + ")");
        sb.AppendLine("  end_force_mode()");
        sb.AppendLine("  textmsg(\"frahan: seated\")");
        sb.AppendLine("  movel(" + Pose(approach) + ", a=" + N(opt.MoveAccel) + ", v=" + N(opt.MoveSpeed) + ")  # retract");
        sb.AppendLine("end");
        return sb.ToString();
    }

    /// <summary>Just the <c>force_mode(...)</c> seat call (caller wraps the
    /// approach / descend / dwell / end_force_mode). Z of <paramref name="seat"/>
    /// is the press direction; only that axis is force-controlled.</summary>
    public static string SeatBlock(RobotPose seat, UrSeatOptions opt = null)
    {
        opt = opt ?? new UrSeatOptions();
        // selection_vector: force-control Z only. wrench: Fz down the seat Z.
        // limits: lateral deviation on stiff axes, descend speed cap on the compliant Z.
        return "force_mode(" + Pose(seat) + ", [0,0,1,0,0,0], "
             + "[0.0,0.0," + N(opt.SeatForce) + ",0.0,0.0,0.0], 2, "
             + "[" + N(opt.MaxLateralDev) + "," + N(opt.MaxLateralDev) + "," + N(opt.DescendSpeed)
             + ",0.05,0.05,0.05])";
    }

    private static string Pose(RobotPose p) =>
        "p[" + N(p.X) + "," + N(p.Y) + "," + N(p.Z) + "," + N(p.Rx) + "," + N(p.Ry) + "," + N(p.Rz) + "]";

    private static string N(double v) => v.ToString("0.#####", CI);

    private static string Ident(string s)
    {
        if (string.IsNullOrEmpty(s)) return "frahan_place";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }
}
