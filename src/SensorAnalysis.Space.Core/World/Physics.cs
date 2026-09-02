using SensorAnalysis.Space.Core.Domain;

namespace SensorAnalysis.Space.Core.World;

/// <summary>Gravity, semi-implicit Euler integration, orientation, and shield-sphere collisions.</summary>
internal static class Physics
{
    /// <summary>Advances velocities, positions, and orientations, then resolves shield overlaps.</summary>
    /// <param name="world">World whose objects are integrated.</param>
    public static void Step(SimWorld world)
    {
        var objects = world.AllObjects;
        var extent = world.Extent;
        var defaultCutoff = world.Options.HalfExtent * world.Options.GravityCutoffFraction;

        foreach (var obj in objects)
        {
            if (obj.IsAnchored)
            {
                obj.Velocity = Vec3.Zero;
                obj.IntegrateOrientation();
                continue;
            }

            if (obj.HasInertia)
            {
                var accel = GravityAt(world, obj.Position, obj, defaultCutoff);
                if (obj.LinearDrag > 0)
                    accel -= obj.Velocity * obj.LinearDrag;

                obj.Velocity += accel;
            }

            obj.Position = world.Wrap(obj.Position + obj.Velocity);
            obj.IntegrateOrientation();
        }

        ResolveCollisions(world, objects, extent);
        UpdateAttachments(world);
    }

    /// <summary>Gravitational acceleration at <paramref name="position"/> from bodies with μ &gt; 0 (no periodic images).</summary>
    /// <param name="world">World containing gravity sources.</param>
    /// <param name="position">Sample point.</param>
    /// <param name="self">Object being accelerated (excluded as a source of its own gravity).</param>
    /// <param name="defaultCutoff">Cutoff used when a body does not specify one.</param>
    /// <returns>Acceleration in world units per tick².</returns>
    public static Vec3 GravityAt(SimWorld world, Vec3 position, WorldObject? self, double defaultCutoff)
    {
        var accel = Vec3.Zero;
        foreach (var body in world.Bodies)
        {
            if (body.Mu <= 0)
                continue;
            if (self is not null && ReferenceEquals(body, self))
                continue;

            var toward = world.Displacement(position, body.Position);
            var r = toward.Length;
            var cutoff = body.GravityCutoff > 0 ? body.GravityCutoff : defaultCutoff;
            if (r < 1e-9 || r > cutoff)
                continue;

            var floor = Math.Max(body.VisualRadius * 0.75, 1e-3);
            if (r < floor)
                r = floor;

            accel += toward.Normalize() * (body.Mu / (r * r));
        }

        return accel;
    }

    /// <summary>Circular-orbit speed around a μ source at radial distance <paramref name="radius"/>.</summary>
    /// <param name="mu">Gravitational parameter.</param>
    /// <param name="radius">Orbital radius.</param>
    /// <returns>Speed <c>sqrt(μ/r)</c>, or 0 when inputs are invalid.</returns>
    public static double CircularSpeed(double mu, double radius) =>
        mu > 0 && radius > 1e-9 ? Math.Sqrt(mu / radius) : 0;

    /// <summary>μ that yields period <paramref name="periodTicks"/> at radius <paramref name="radius"/>.</summary>
    /// <param name="radius">Orbital radius.</param>
    /// <param name="periodTicks">Desired period in ticks.</param>
    /// <returns>μ = ω² r³.</returns>
    public static double MuForPeriod(double radius, double periodTicks)
    {
        if (radius <= 0 || periodTicks <= 0)
            return 0;
        var omega = Math.Tau / periodTicks;
        return omega * omega * radius * radius * radius;
    }

    private static void ResolveCollisions(SimWorld world, IReadOnlyList<WorldObject> objects, double extent)
    {
        var n = objects.Count;
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var a = objects[i];
                var b = objects[j];
                var minDist = a.ShieldRadius + b.ShieldRadius;
                if (minDist <= 0)
                    continue;

                var delta = world.Displacement(a.Position, b.Position);
                var dist = delta.Length;
                if (dist >= minDist || dist < 1e-12)
                    continue;

                var normal = delta / dist;
                var overlap = minDist - dist;
                Separate(world, a, b, normal, overlap);

                var e = Math.Clamp(Math.Min(a.Restitution, b.Restitution), 0, 1);
                Bounce(a, b, normal, e);
            }
        }
    }

    private static void Separate(SimWorld world, WorldObject a, WorldObject b, Vec3 normal, double overlap)
    {
        if (a.IsAnchored && b.IsAnchored)
            return;

        if (a.IsAnchored)
        {
            b.Position = world.Wrap(b.Position + normal * overlap);
            return;
        }

        if (b.IsAnchored)
        {
            a.Position = world.Wrap(a.Position - normal * overlap);
            return;
        }

        var invA = 1.0 / Math.Max(a.Mass, 1e-9);
        var invB = 1.0 / Math.Max(b.Mass, 1e-9);
        var sum = invA + invB;
        a.Position = world.Wrap(a.Position - normal * (overlap * invA / sum));
        b.Position = world.Wrap(b.Position + normal * (overlap * invB / sum));
    }

    private static void Bounce(WorldObject a, WorldObject b, Vec3 normal, double restitution)
    {
        var rel = b.Velocity - a.Velocity;
        var closing = Vec3.Dot(rel, normal);
        if (closing >= 0)
            return;

        if (a.IsAnchored && b.IsAnchored)
            return;

        if (a.IsAnchored)
        {
            b.Velocity -= normal * ((1 + restitution) * closing);
            return;
        }

        if (b.IsAnchored)
        {
            a.Velocity += normal * ((1 + restitution) * closing);
            return;
        }

        var invA = 1.0 / Math.Max(a.Mass, 1e-9);
        var invB = 1.0 / Math.Max(b.Mass, 1e-9);
        var impulse = -(1 + restitution) * closing / (invA + invB);
        a.Velocity -= normal * (impulse * invA);
        b.Velocity += normal * (impulse * invB);
    }

    private static void UpdateAttachments(SimWorld world)
    {
        foreach (var incident in world.Incidents)
        {
            if (incident.ParentId is not { } parentId)
                continue;

            var parent = world.FindObject(parentId);
            if (parent is null)
                continue;

            var offset = parent.Right * incident.LocalOffset.X
                         + parent.Up * incident.LocalOffset.Y
                         + parent.Forward * incident.LocalOffset.Z;
            incident.Position = world.Wrap(parent.Position + offset);
            incident.Velocity = parent.Velocity;
            incident.Forward = parent.Forward;
            incident.Up = parent.Up;
        }
    }
}
