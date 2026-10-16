using System;
using Newtonsoft.Json;

namespace GuidedReality
{
    [Serializable] public sealed class InstructionStep
    {
        public string instruction;
        public int visual_type;
        public string[] key_components;
        public string Target => key_components[0];
        public string Action => key_components.Length > 1 ? key_components[1] : "";
        public string Model => visual_type == 4 ? key_components[2] : HandModel(Action);
        public static string HandModel(string gesture) => gesture switch {
            "pinch" => "OculusHand_R_modifymodel", "poke" => "poke", "squeeze" => "Squeez",
            "hold" => "hold", "palm" => "Palm", "palmVerticle" => "palmverticle", "hook" => "hook1",
            _ => throw new Exception("Unknown hand gesture: " + gesture)
        };
        public static int TimerSeconds(string value)
        {
            var parts = value.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int m) || !int.TryParse(parts[1], out int s) || m < 0 || m > 1440 || s < 0 || s > 59)
                throw new Exception("Timer must use mm:ss.");
            return m * 60 + s;
        }
        public void Validate()
        {
            int count = visual_type == 4 ? 3 : visual_type == 1 ? 1 : 2;
            if (visual_type < 1 || visual_type > 5 || string.IsNullOrWhiteSpace(instruction) || key_components == null || key_components.Length < count || string.IsNullOrWhiteSpace(Target))
                throw new Exception("Invalid instruction or visual_type in instruction JSON.");
            if (visual_type == 3) _ = Model;
            if (visual_type == 5) _ = TimerSeconds(Action);
            if (visual_type == 4 && !string.IsNullOrWhiteSpace(Action) && Array.IndexOf(new[]{"up and down","left and right","rotate","clockwise","counterclockwise","no movement"},Action)<0)
                throw new Exception("Unknown tool movement: " + Action);
            if (visual_type == 4 && !System.Text.RegularExpressions.Regex.IsMatch(Model, "^[a-zA-Z0-9_]+$"))
                throw new Exception("Invalid tool model name.");
        }
    }
    [Serializable] public sealed class InstructionDocument
    {
        public InstructionStep[] instructions;
        public static InstructionDocument Parse(string json)
        {
            var doc = JsonConvert.DeserializeObject<InstructionDocument>(json);
            if (doc?.instructions == null || doc.instructions.Length == 0) throw new Exception("Instruction JSON contains no steps.");
            foreach (var step in doc.instructions) { if (step == null) throw new Exception("Empty instruction."); step.Validate(); }
            return doc;
        }
    }
}
