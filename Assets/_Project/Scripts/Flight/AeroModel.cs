using System;

namespace GateRush.Flight
{
    /// <summary>Body-axis linear + quadratic drag and rotational damping.</summary>
    public static class AeroModel
    {
        /// <summary>Drag force in body frame for a body-frame airspeed.</summary>
        public static Vec3 DragForce(in AeroSettings a, Vec3 vBody)
        {
            return new Vec3(
                -(a.LinearDrag.x * vBody.x + a.QuadraticDrag.x * vBody.x * MathF.Abs(vBody.x)),
                -(a.LinearDrag.y * vBody.y + a.QuadraticDrag.y * vBody.y * MathF.Abs(vBody.y)),
                -(a.LinearDrag.z * vBody.z + a.QuadraticDrag.z * vBody.z * MathF.Abs(vBody.z)));
        }

        /// <summary>Rotational damping torque in body frame.</summary>
        public static Vec3 DampingTorque(in AeroSettings a, Vec3 omegaBody) => -Vec3.Scale(a.AngularDrag, omegaBody);
    }
}
