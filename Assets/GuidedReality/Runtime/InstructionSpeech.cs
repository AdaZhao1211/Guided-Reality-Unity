using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GuidedReality
{
    // Clips are generated ahead of time by Tools/generate_narration.py.
    [RequireComponent(typeof(AudioSource))]
    public sealed class InstructionSpeech : MonoBehaviour
    {
        AudioSource source;
        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
        }
        public static string ResourcePath(string text)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.Trim()));
            var name = new StringBuilder(64);
            foreach (byte b in hash) name.Append(b.ToString("x2"));
            return "InstructionNarration/" + name;
        }
        public void Speak(string text)
        {
            source.Stop();
            source.clip = null;
            if (string.IsNullOrWhiteSpace(text)) return;
            var clip = Resources.Load<AudioClip>(ResourcePath(text));
            if (!clip) {
                Debug.LogWarning("GuidedReality: No bundled narration for this instruction. Run Tools/generate_narration.py after editing 3DPrinter.json.");
                return;
            }
            source.clip = clip;
            source.Play();
            Debug.Log("GuidedReality narration: " + text);
        }
        void OnApplicationPause(bool paused) { if (paused && source) source.Stop(); }
        void OnDisable() { if (source) source.Stop(); }
    }
}
