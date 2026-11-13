/*!
 * «1000» — процедурный звук на WebAudio.
 * Никаких файлов: короткие синтезированные шорохи, щелчки и аккорды.
 */
(function (root) {
  'use strict';

  const STORAGE_KEY = 'thousand.sound';
  const AudioCtor = root.AudioContext || root.webkitAudioContext;

  class Sound {
    constructor() {
      this.ctx = null;
      this.master = null;
      this.enabled = true;
      this.noiseBuffer = null;
      try {
        const saved = root.localStorage.getItem(STORAGE_KEY);
        if (saved === '0') this.enabled = false;
      } catch (_) {
        /* localStorage может быть недоступен */
      }
    }

    init() {
      if (!AudioCtor) return false;
      if (!this.ctx) {
        this.ctx = new AudioCtor();
        this.master = this.ctx.createGain();
        this.master.gain.value = 0.22;
        this.master.connect(this.ctx.destination);
      }
      if (this.ctx.state === 'suspended') this.ctx.resume().catch(() => {});
      return true;
    }

    setEnabled(value) {
      this.enabled = Boolean(value);
      try {
        root.localStorage.setItem(STORAGE_KEY, this.enabled ? '1' : '0');
      } catch (_) {
        /* ignore */
      }
      if (this.master) this.master.gain.value = this.enabled ? 0.22 : 0;
    }

    toggle() {
      this.setEnabled(!this.enabled);
      if (this.enabled) {
        this.init();
        this.play('click');
      }
      return this.enabled;
    }

    noise(duration, gain, filterFreq, q) {
      if (!this.ctx) return;
      const ctx = this.ctx;
      if (!this.noiseBuffer) {
        const length = Math.floor(ctx.sampleRate * 0.5);
        this.noiseBuffer = ctx.createBuffer(1, length, ctx.sampleRate);
        const data = this.noiseBuffer.getChannelData(0);
        for (let i = 0; i < length; i += 1) data[i] = Math.random() * 2 - 1;
      }
      const src = ctx.createBufferSource();
      src.buffer = this.noiseBuffer;
      const filter = ctx.createBiquadFilter();
      filter.type = 'bandpass';
      filter.frequency.value = filterFreq;
      filter.Q.value = q || 0.8;
      const amp = ctx.createGain();
      const now = ctx.currentTime;
      amp.gain.setValueAtTime(0.0001, now);
      amp.gain.exponentialRampToValueAtTime(gain, now + 0.008);
      amp.gain.exponentialRampToValueAtTime(0.0001, now + duration);
      src.connect(filter);
      filter.connect(amp);
      amp.connect(this.master);
      src.start(now);
      src.stop(now + duration + 0.02);
    }

    tone(freq, duration, gain, type, when, slideTo) {
      if (!this.ctx) return;
      const ctx = this.ctx;
      const now = ctx.currentTime + (when || 0);
      const osc = ctx.createOscillator();
      const amp = ctx.createGain();
      osc.type = type || 'sine';
      osc.frequency.setValueAtTime(freq, now);
      if (slideTo) osc.frequency.exponentialRampToValueAtTime(slideTo, now + duration);
      amp.gain.setValueAtTime(0.0001, now);
      amp.gain.exponentialRampToValueAtTime(gain, now + 0.012);
      amp.gain.exponentialRampToValueAtTime(0.0001, now + duration);
      osc.connect(amp);
      amp.connect(this.master);
      osc.start(now);
      osc.stop(now + duration + 0.03);
    }

    play(name) {
      if (!this.enabled) return;
      if (!this.init()) return;
      const ctx = this.ctx;
      if (!ctx) return;
      if (ctx.state === 'suspended') ctx.resume().catch(() => {});

      switch (name) {
        case 'click':
          this.tone(520, 0.045, 0.055, 'triangle');
          break;
        case 'deal':
          this.noise(0.075, 0.12, 1900, 1.1);
          this.tone(190, 0.04, 0.025, 'sine');
          break;
        case 'card':
          this.noise(0.085, 0.16, 2400, 1.3);
          this.tone(140, 0.05, 0.03, 'sine');
          break;
        case 'bid':
          this.tone(660, 0.07, 0.07, 'triangle');
          this.tone(880, 0.09, 0.045, 'sine', 0.055);
          break;
        case 'pass':
          this.tone(330, 0.1, 0.045, 'sine');
          this.tone(262, 0.12, 0.035, 'sine', 0.06);
          break;
        case 'trick':
          this.tone(523.25, 0.12, 0.055, 'sine');
          this.tone(659.25, 0.14, 0.05, 'sine', 0.07);
          this.tone(783.99, 0.18, 0.04, 'sine', 0.14);
          break;
        case 'marriage':
          [523.25, 659.25, 783.99, 1046.5].forEach((f, i) => {
            this.tone(f, 0.16, 0.055, 'triangle', i * 0.075);
          });
          break;
        case 'score':
          this.tone(880, 0.08, 0.04, 'square');
          this.tone(1174.66, 0.12, 0.03, 'sine', 0.05);
          break;
        case 'win':
          [392, 523.25, 659.25, 783.99, 1046.5].forEach((f, i) => {
            this.tone(f, 0.5, 0.06, 'triangle', i * 0.11);
          });
          this.noise(0.35, 0.03, 1200, 0.7);
          break;
        case 'lose':
          this.tone(392, 0.22, 0.05, 'sine');
          this.tone(311.13, 0.26, 0.045, 'sine', 0.16);
          this.tone(233.08, 0.42, 0.04, 'sine', 0.32);
          break;
        case 'warn':
          this.tone(220, 0.12, 0.05, 'sawtooth');
          this.tone(180, 0.16, 0.04, 'sawtooth', 0.09);
          break;
        default:
          break;
      }
    }
  }

  root.ThousandSound = new Sound();
})(window);
