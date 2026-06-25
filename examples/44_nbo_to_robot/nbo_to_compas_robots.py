# Frahan NBO -> COMPAS robot SIMULATION / IK test  (Rhino 8 GhPython, Python 3).
# =============================================================================
# Paste into a GhPython / Python 3 Script component in Rhino 8. This is the
# robot-workflow consumer of the Frahan NBO planner: it takes the TCP target
# PLANES that Frahan's "Next-Best-Object Pose -> Robot Frame" component already
# emits and runs an IN-PROCESS forward/inverse-kinematics simulation on a UR10e.
# It builds NO Frahan-side robot component -- the COMPAS Python libraries do the
# kinematics. Hardware stays dormant: this is a reachability + sim TEST only.
#
# COMPAS package split (verified against compas_robots 1.0.1 / compas_fab 1.1.0):
#   * compas_robots  = robot MODEL + URDF load + forward kinematics + visualization.
#                      It has NO inverse kinematics.
#   * compas_fab     = analytic UR inverse kinematics (closed-form, in-process,
#                      NO ROS / NO Docker): AnalyticalInverseKinematics, key "ur10e".
#   * compas         = compas_rhino.conversions  (Rhino Plane <-> COMPAS Frame).
# Units are METERS + RADIANS (REP-103); Frahan emits metres already.
#
# Install (top of this component, recommended), or pip into the rhino interpreter:
#   #! python3
#   # r: compas_fab==1.1.0        (pulls compas + compas_robots; 1.1.0 = the simple
#                                  IK form used below; 2.0.x changed the IK API -- see TODO)
#
# Inputs  (GH):  place_planes : list[Rhino.Geometry.Plane]  -- Frahan place TCP planes
#                urdf_path    : str                         -- path to a flat UR10e URDF (metres)
# Outputs (GH):  reachable      : list[bool]                -- IK solved per plane
#                configs        : list[str]                 -- joint angles (deg) per reachable plane
#                reached_planes : list[Rhino.Geometry.Plane]-- FK round-trip (orientation check)
#                robot_meshes   : list[Mesh]                -- arm at the LAST reachable pose (sim viz)
#                report         : str
# =============================================================================

#! python3
# r: compas_fab==1.1.0

import math

reachable = []
configs = []
reached_planes = []
robot_meshes = []
report = ""

try:
    from compas_robots import RobotModel
    from compas_rhino.conversions import plane_to_compas_frame, frame_to_rhino_plane
    from compas_fab.backends import AnalyticalInverseKinematics
except Exception as e:
    report = ("COMPAS not available in this interpreter. Add the requirement marker\n"
              "  #! python3\n  # r: compas_fab==1.1.0\n"
              "at the very top of this component, or pip install into "
              "%USERPROFILE%\\.rhinocode\\py39-rh8\\python.exe. Error: " + str(e))
    raise

if not place_planes:
    report = "No place_planes. Wire Frahan 'Next-Best-Object Pose -> Robot Frame' -> Place Frames."
elif not urdf_path:
    report = "No urdf_path. Provide a flat UR10e URDF (metres). ur_description has ur10e.urdf.xacro -> bake to urdf."
else:
    # --- model (UR10e has NO built-in factory: must load a URDF; metres+radians) ---
    model = RobotModel.from_urdf_file(urdf_path)          # verified ctor (NOT from_urdf_cls)
    ik = AnalyticalInverseKinematics()                    # analytic UR IK = compas_fab, in-process

    last_config = None
    for rh_plane in place_planes:
        # Rhino Plane -> COMPAS Frame. Use plane_to_compas_FRAME (keeps the x/y axes =
        # the TCP orientation). plane_to_compas returns a Plane (point+normal) and drops
        # the in-plane rotation -- do not use it for a TCP target.
        target = plane_to_compas_frame(rh_plane)

        # analytic IK: yields up to 8 (joint_positions, joint_names); empty => unreachable.
        sols = list(ik.inverse_kinematics(model, target,
                                          options={"solver": "ur10e", "keep_order": False}))
        if not sols:
            reachable.append(False)
            continue

        jp, jn = sols[0]
        config = model.zero_configuration()
        for name, value in zip(jn, jp):
            config[name] = value                          # radians
        last_config = config

        # FK confirm (forward_kinematics(joint_state) -> Frame at the end effector).
        fk_frame = model.forward_kinematics(config)
        reached_planes.append(frame_to_rhino_plane(fk_frame))
        configs.append(", ".join("%.1f" % math.degrees(v) for v in jp))
        reachable.append(True)

    # --- sim viz: draw the arm at the last reachable configuration ---
    # NOTE: robot_meshes need the URDF's visual geometry loaded. If the model was built
    # kinematics-only, draw() yields nothing -> load geometry first. The exact LOCAL
    # mesh-loader class was not verified; for a github-hosted package use
    # GithubPackageMeshLoader. TODO: confirm the local loader before relying on this.
    if last_config is not None:
        try:
            from compas.scene import Scene
            scene = Scene()
            obj = scene.add(model)
            obj.update(last_config)
            drawn = scene.draw()
            robot_meshes = drawn if isinstance(drawn, list) else [drawn]
        except Exception as e:
            report += "[viz skipped: %s] " % str(e)

    n = len(place_planes)
    ok = sum(1 for r in reachable if r)
    report = ("UR10e analytic-IK sim: %d/%d planes reachable. " % (ok, n)) + report
    report += "\\ncompas_robots=model+FK+viz; compas_fab=analytic IK (in-process, no ROS). Hardware dormant."

# TODO(compas_fab 2.0.x): replace AnalyticalInverseKinematics().inverse_kinematics(...) with
#   AnalyticalKinematicsPlanner(UR10eKinematics()) + a RobotCell + FrameTarget +
#   iter_inverse_kinematics(...). Pin compas_fab==1.1.0 to run this file unmodified.
