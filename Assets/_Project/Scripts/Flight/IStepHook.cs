namespace GateRush.Flight
{
    /// <summary>Called after each fixed sub-step. Return false to stop sub-stepping for this frame (e.g. after a crash).</summary>
    public interface IStepHook
    {
        bool AfterStep(ref QuadState s, in QuadState previous, float dt);
    }
}
