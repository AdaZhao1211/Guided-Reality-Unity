using UnityEngine;

namespace GuidedReality
{
    public enum StepCommand { None, Load, Previous, Next, Retry }
    public sealed class ControllerStepInput
    {
        bool bWasDown, aWasDown, stickLatched;
        public StepCommand Read(bool b, bool a, Vector2 stick)
        {
            bool load=b && !bWasDown, retry=a && !aWasDown;
            bWasDown=b; aWasDown=a;
            if (stick.magnitude < .25f) stickLatched=false;
            StepCommand navigation=StepCommand.None;
            if (!stickLatched && Mathf.Abs(stick.x) >= .7f && Mathf.Abs(stick.x)>Mathf.Abs(stick.y)) {
                stickLatched=true;
                navigation=stick.x>0 ? StepCommand.Next : StepCommand.Previous;
            }
            // Consume edges even while processing so held controls never queue surprise steps.
            return load ? StepCommand.Load : navigation!=StepCommand.None ? navigation : retry ? StepCommand.Retry : StepCommand.None;
        }
    }
}
