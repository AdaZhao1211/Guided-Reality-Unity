package com.ada.guidedreality;

import android.content.Context;
import android.media.AudioAttributes;
import android.os.Handler;
import android.os.Looper;
import android.speech.tts.TextToSpeech;
import android.speech.tts.UtteranceProgressListener;
import java.util.Locale;

/** All engine lifecycle operations run on Android's main thread. */
public final class GuidanceSpeech {
    private final Handler main = new Handler(Looper.getMainLooper());
    private TextToSpeech engine;
    private boolean ready, closed;
    private String pending;
    private volatile String status = "Initializing";
    private volatile String activeUtterance = "";
    private long sequence;

    public GuidanceSpeech(Context context) {
        Context app = context.getApplicationContext();
        main.post(() -> {
            if (closed) return;
            try {
                engine = new TextToSpeech(app, result -> main.post(() -> initialized(result)));
            } catch (Exception e) { status = "Error: " + e.getMessage(); }
        });
    }
    private void initialized(int result) {
        if (closed || engine == null) return;
        if (result != TextToSpeech.SUCCESS) { status = "Error: speech engine unavailable"; return; }
        int language = engine.setLanguage(Locale.US);
        if (language < 0) { status = "Error: English voice data unavailable"; return; }
        engine.setAudioAttributes(new AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build());
        engine.setOnUtteranceProgressListener(new UtteranceProgressListener() {
            @Override public void onStart(String id) { if (id.equals(activeUtterance)) status = "Speaking"; }
            @Override public void onDone(String id) { if (id.equals(activeUtterance)) status = "Ready"; }
            @Override public void onError(String id) { if (id.equals(activeUtterance)) status = "Error: synthesis failed"; }
            @Override public void onError(String id, int code) { if (id.equals(activeUtterance)) status = "Error: synthesis failed (" + code + ")"; }
        });
        ready = true;
        status = "Ready";
        speakPending();
    }
    public String getStatus() { return status; }
    public void speak(String text) {
        main.post(() -> {
            if (closed) return;
            pending = text; // Only the most recently requested step survives initialization.
            speakPending();
        });
    }
    private void speakPending() {
        if (!ready || pending == null || closed) return;
        String text = pending;
        pending = null;
        activeUtterance = "instruction-" + (++sequence);
        // Interrupt the previous step instead of overlapping or building a speech queue.
        int result = engine.speak(text, TextToSpeech.QUEUE_FLUSH, null, activeUtterance);
        status = result == TextToSpeech.SUCCESS ? "Queued" : "Error: speech request rejected";
    }
    public void stop() {
        main.post(() -> {
            pending = null;
            activeUtterance = "";
            if (engine != null) engine.stop();
        });
    }
    public void shutdown() {
        main.post(() -> {
            closed = true; ready = false; pending = null; activeUtterance = "";
            if (engine != null) { engine.stop(); engine.shutdown(); engine = null; }
        });
    }
}
