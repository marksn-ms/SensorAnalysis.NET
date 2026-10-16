using SensorAnalysis.Core.Domain;

namespace SensorAnalysis.Hallway;

/// <summary>
/// One windshield contact after a passage device has chosen which quantities to expose.
/// Side is +1 when the contact sits to the right of the student's heading.
/// </summary>
public readonly record struct PassageCue(double EyeUrgency, double MissUrgency, double Side, bool Hold);

/// <summary>Which sensor wrapper a student uses. The plan and the reward stay the same.</summary>
public enum PassageDeviceKind
{
    /// <summary>Range, range rate, and bearing rate inside the forward sector.</summary>
    Eye = 0,

    /// <summary>Eye features plus predicted miss distance and time to closest approach.</summary>
    Math = 1,

    /// <summary>Longer range, noisy bearing, exact range rate.</summary>
    Radar = 2,

    /// <summary>Eye features plus a short hold token on the radio.</summary>
    Radio = 3
}

/// <summary>Relative-motion features shared by every passage device.</summary>
public static class PassageGeometry
{
    /// <summary>
    /// Range rate, signed bearing rate, miss distance, and time to closest approach
    /// for a contact at <paramref name="relativePosition"/> with <paramref name="relativeVelocity"/>.
    /// Positive range rate is opening. Positive bearing rate slides toward +Y when the
    /// line of sight is along +X.
    /// </summary>
    public static (double RangeRate, double BearingRate, double Miss, double Time) Approach(
        Vec2 relativePosition,
        Vec2 relativeVelocity)
    {
        var range = Math.Max(1e-6, relativePosition.Length);
        var rangeRate = Dot(relativePosition, relativeVelocity) / range;
        var bearingRate = Cross(relativePosition, relativeVelocity) / (range * range);
        var speed2 = Dot(relativeVelocity, relativeVelocity);
        if (speed2 < 1e-8)
            return (rangeRate, bearingRate, range, 0);

        var time = -Dot(relativePosition, relativeVelocity) / speed2;
        if (time < 0)
            return (rangeRate, bearingRate, range, time);

        var closest = relativePosition + relativeVelocity * time;
        return (rangeRate, bearingRate, closest.Length, time);
    }

    /// <summary>Urgency from closing speed, steady bearing, and proximity. About 1 is an imminent hit.</summary>
    public static double EyeUrgency(double range, double rangeRate, double bearingRate)
    {
        var closing = Math.Max(0, -rangeRate);
        var steady = 1.0 / (1.0 + Math.Abs(bearingRate) * 40);
        var proximity = 1.0 / (1.0 + range / 36.0);
        return steady * proximity * (0.25 + closing / (closing + 4));
    }

    /// <summary>Bearing of <paramref name="relativePosition"/> relative to <paramref name="heading"/>, in radians.</summary>
    public static double RelativeBearing(Vec2 heading, Vec2 relativePosition)
    {
        var headingAngle = Math.Atan2(heading.Y, heading.X);
        var bearing = Math.Atan2(relativePosition.Y, relativePosition.X);
        return Wrap(bearing - headingAngle);
    }

    /// <summary>True when the contact lies within 100 degrees of the heading.</summary>
    public static bool InWindshield(double relativeBearing) => Math.Abs(relativeBearing) <= 100 * Math.PI / 180;

    /// <summary>+1 to the right of <paramref name="heading"/>, -1 to the left, 0 when dead ahead.</summary>
    public static double Side(Vec2 heading, Vec2 relativePosition)
    {
        var cross = Cross(heading, relativePosition);
        if (Math.Abs(cross) < 1e-6)
            return 0;
        return Math.Sign(cross);
    }

    /// <summary>Wrap an angle into (-pi, pi].</summary>
    public static double Wrap(double angle)
    {
        var wrapped = angle % (Math.PI * 2);
        if (wrapped > Math.PI) wrapped -= Math.PI * 2;
        if (wrapped <= -Math.PI) wrapped += Math.PI * 2;
        return wrapped;
    }

    private static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    private static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
}

/// <summary>Builds windshield cues for one device kind.</summary>
public static class PassageDevices
{
    /// <summary>Parse a deployer device name. Unknown names are <see cref="PassageDeviceKind.Eye"/>.</summary>
    public static PassageDeviceKind Parse(string? text)
    {
        if (Enum.TryParse<PassageDeviceKind>(text, ignoreCase: true, out var kind))
            return kind;
        return PassageDeviceKind.Eye;
    }

    /// <summary>Forward-sector cues from peers, wall surfaces, and optional radio holds.</summary>
    public static List<PassageCue> Sense(
        PassageDeviceKind kind,
        HallwayStudentAgent self,
        AgentObservation observation)
    {
        var heading = self.Heading.Length < 1e-6 ? new Vec2(1, 0) : self.Heading.Normalize();
        var selfVelocity = heading * self.Velocity;
        var cues = new List<PassageCue>();
        var index = 0;

        foreach (var peer in observation.NearbyAgents)
        {
            var relativeVelocity = peer.Velocity - selfVelocity;
            AddCue(kind, self, observation, heading, peer.Position, relativeVelocity, hold: false, ref index, cues);
        }

        foreach (var body in observation.NearbyBodies)
        {
            var relativeVelocity = selfVelocity * -1;
            AddCue(kind, self, observation, heading, body.NearestSurface, relativeVelocity, hold: false, ref index, cues);
        }

        if (kind == PassageDeviceKind.Radio)
        {
            foreach (var message in observation.NearbyMessages)
            {
                if (message.Payload.StartsWith("hold", StringComparison.OrdinalIgnoreCase))
                    cues.Add(new PassageCue(0, 0, 0, Hold: true));
            }
        }

        return cues;
    }

    private static void AddCue(
        PassageDeviceKind kind,
        HallwayStudentAgent self,
        AgentObservation observation,
        Vec2 heading,
        Vec2 relativePosition,
        Vec2 relativeVelocity,
        bool hold,
        ref int index,
        List<PassageCue> cues)
    {
        if (relativePosition.Length < 1e-6)
            return;

        var sensed = relativePosition;
        if (kind == PassageDeviceKind.Radar)
        {
            var noise = Math.Sin(observation.Tick * 12.9898 + self.Id * 78.233 + index) * 0.12;
            var angle = Math.Atan2(relativePosition.Y, relativePosition.X) + noise;
            var range = relativePosition.Length;
            sensed = new Vec2(Math.Cos(angle) * range, Math.Sin(angle) * range);
        }

        index++;
        var bearing = PassageGeometry.RelativeBearing(heading, sensed);
        if (!PassageGeometry.InWindshield(bearing))
            return;

        var motion = PassageGeometry.Approach(relativePosition, relativeVelocity);
        var eye = PassageGeometry.EyeUrgency(relativePosition.Length, motion.RangeRate, motion.BearingRate);
        var miss = 0.0;
        if (kind == PassageDeviceKind.Math && motion.Time >= 0 && motion.Time < 40)
            miss = 1.0 / (1.0 + motion.Miss / Math.Max(1, self.BodyRadius));

        cues.Add(new PassageCue(eye, miss, PassageGeometry.Side(heading, sensed), hold));
    }
}
