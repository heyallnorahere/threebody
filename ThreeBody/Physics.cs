using CodePlayground;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ThreeBody
{
    public struct Body
    {
        public Body()
        {
            Position = Vector3.Zero;
            Velocity = Vector3.Zero;
            Acceleration = Vector3.Zero;
            Radius = 1f;
            Density = 1f;
            Color = Vector4.One;
        }

        public Vector3 Position, Velocity, Acceleration;
        public float Radius, Density;
        public Vector4 Color;
    }

    internal struct MassCache
    {
        public float Mass, Volume, Radius, Density;
    }

    public static class Physics
    {
        public const float G = 6.674e-11f;

        private static readonly Registry sRegistry;
        private static readonly Dictionary<ulong, MassCache> sMasses;

        static Physics()
        {
            sRegistry = new Registry();
            sMasses = new Dictionary<ulong, MassCache>();
        }

        public static Registry Registry => sRegistry;

        public static float SphereVolume(float radius) => MathF.Pow(radius, 3f) * MathF.PI * 4f / 3f;
        public static float GetBodyMass(ulong entity)
        {
            ref var body = ref sRegistry.Get<Body>(entity).Value;
            if (sMasses.TryGetValue(entity, out MassCache cache))
            {
                if (MathF.Abs(cache.Radius - body.Radius) < float.Epsilon &&
                    MathF.Abs(cache.Density - body.Density) < float.Epsilon)
                {
                    return cache.Mass;
                }
            }

            float volume = SphereVolume(body.Radius);
            float mass = volume * body.Density; // density is kg*m^-3 in this case (3d physics)

            sMasses[entity] = new MassCache
            {
                Mass = mass,
                Volume = volume,
                Radius = body.Radius,
                Density = body.Density
            };

            return mass;
        }

        private static void AddForces()
        {
            using var addForcesEvent = Profiler.Event();

            // forgive the pseudo-math
            // F_b=sum(G*m_b*m_n/(r_b-r_n)^2)
            //    =G*m_b*sum(m_n/(r_b-r_n)^2)
            // ill be honest im too tired to do this right now

            var masses = new Dictionary<ulong, float>();
            var entities = sRegistry.View(typeof(Body));

            foreach (var entity in entities)
            {
                ref var body = ref sRegistry.Get<Body>(entity).Value;
                foreach (var otherEntity in entities)
                {
                    if (entity == otherEntity)
                    {
                        continue;
                    }

                    ref var otherBody = ref sRegistry.Get<Body>(otherEntity).Value;
                    var bodyToOther = otherBody.Position - body.Position;
                    var direction = Vector3.Normalize(bodyToOther);
                    var distanceSquared = bodyToOther.LengthSquared();

                    if (MathF.Sqrt(distanceSquared) < float.Max(body.Radius, otherBody.Radius))
                    {
                        continue;
                    }

                    float mass = GetBodyMass(otherEntity);
                    float g = G * mass / distanceSquared;
                    body.Acceleration += g * direction;
                }
            }
        }

        private static void UpdateBodies(double delta)
        {
            using var updateBodiesEvent = Profiler.Event();
            foreach (var entity in sRegistry.View(typeof(Body)))
            {
                ref var body = ref sRegistry.Get<Body>(entity).Value;
                body.Velocity += body.Acceleration * (float)delta;
                body.Position += body.Velocity * (float)delta;
                body.Acceleration = Vector3.Zero;
            }
        }

        public static void Update(double delta)
        {
            using var updateEvent = Profiler.Event();

            AddForces();
            UpdateBodies(delta);
        }
    }
}