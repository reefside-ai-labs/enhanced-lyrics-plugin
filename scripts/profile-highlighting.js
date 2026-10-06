// Diagnostic: run this expression in the browser console during visible local
// audio playback with Enhanced Lyrics active. It only samples clocks and frames.
// Keep the preview visible; T3 recording keeps animation frames running reliably.
(async () => {
    const lyrics = document.querySelector('braccato-lyrics');
    const audio = document.querySelector('audio');
    if (!lyrics || !audio || audio.paused || document.hidden) {
        throw new Error('Requires visible, active local audio playback with enhanced lyrics');
    }
    const samples = [];
    await new Promise(resolve => {
        let done = false;
        setTimeout(() => { done = true; resolve(); }, 3000);
        function frame(now) {
            if (done) return;
            samples.push({ now, lyric: lyrics.currentTime, audio: audio.currentTime });
            requestAnimationFrame(frame);
        }
        requestAnimationFrame(frame);
    });
    if (samples.length < 60) {
        return { verdict: 'inconclusive', frames: samples.length,
            reason: 'Too few animation frames; keep the browser visible and retry' };
    }
    const deltas = samples.slice(1).map((s, i) => ({
        frame: s.now - samples[i].now,
        lyric: (s.lyric - samples[i].lyric) * 1000,
        audio: (s.audio - samples[i].audio) * 1000
    }));
    // Exclude pauses, seeks and looping boundaries from the clock stall ratio.
    const advancing = deltas.filter(d => d.audio > 4 && d.audio < 100);
    if (!advancing.length) return { verdict: 'inconclusive', reason: 'Audio did not advance', frames: samples.length };
    const sorted = deltas.map(d => d.frame).sort((a, b) => a - b);
    const stalledFraction = advancing.filter(d => Math.abs(d.lyric) < 0.01).length / advancing.length;
    return {
        verdict: stalledFraction < 0.2 ? 'pass' : 'fail',
        frames: samples.length,
        frameMedianMs: sorted[Math.floor(sorted.length / 2)],
        frameP95Ms: sorted[Math.floor(sorted.length * 0.95)],
        framesOver33Ms: deltas.filter(d => d.frame > 33).length,
        stalledFraction,
        maxLyricStepMs: Math.max(...advancing.map(d => d.lyric))
    };
})();
