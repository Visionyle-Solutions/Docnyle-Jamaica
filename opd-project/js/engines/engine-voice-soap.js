/* ============================================================
   engines/engine-voice-soap.js  v1.0
   Hands-free Voice SOAP dictation using the native Web Speech
   Recognition API. Supports trigger parsing:
     "Subjective..." → routes to Subjective textarea
     "Objective..." → routes to Objective textarea
     "Plan..." → routes to Plan textarea
   Phase 4 destination: Services/VoiceDictationService.cs (Blazor JS interop)
   ============================================================ */

(function () {
  'use strict';

  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  const _state = {
    recognition: null,
    isListening: false,
    activeTarget: null, /* textarea element to insert into */
    onStatusChange: null,
  };

  function isSupported() {
    return !!SpeechRecognition;
  }

  function init(options) {
    if (!isSupported()) return false;
    _state.onStatusChange = options.onStatusChange || (() => {});
    _state.recognition = new SpeechRecognition();
    _state.recognition.continuous = true;
    _state.recognition.interimResults = true;
    _state.recognition.lang = options.lang || 'en-IN';

    let finalTranscript = '';

    _state.recognition.onresult = (event) => {
      let interim = '';
      for (let i = event.resultIndex; i < event.results.length; i++) {
        const transcript = event.results[i][0].transcript;
        if (event.results[i].isFinal) {
          finalTranscript += transcript + ' ';
          _processTranscript(transcript.trim());
        } else {
          interim += transcript;
        }
      }
      _state.onStatusChange({ isListening: true, interim, final: finalTranscript.trim() });
    };

    _state.recognition.onerror = (event) => {
      if (event.error === 'no-speech' || event.error === 'aborted') return;
      console.warn('Speech recognition error:', event.error);
      _state.onStatusChange({ isListening: false, error: event.error });
    };

    _state.recognition.onend = () => {
      /* Auto-restart if still supposed to be listening (browser stops after silence) */
      if (_state.isListening) {
        try { _state.recognition.start(); } catch (e) { /* ignore */ }
      } else {
        _state.onStatusChange({ isListening: false });
      }
    };

    return true;
  }

  function _processTranscript(text) {
    const lower = text.toLowerCase();

    /* Trigger parsing: detect section keywords and switch target */
    if (lower.startsWith('subjective') || lower.startsWith('history') || lower.startsWith('patient reports')) {
      _switchTarget('soapSubjective');
      text = text.replace(/^(subjective|history|patient reports)\s*/i, '');
    } else if (lower.startsWith('objective') || lower.startsWith('examination') || lower.startsWith('on examination')) {
      _switchTarget('soapObjective');
      text = text.replace(/^(objective|examination|on examination)\s*/i, '');
    } else if (lower.startsWith('plan') || lower.startsWith('treatment') || lower.startsWith('advised')) {
      _switchTarget('soapPlan');
      text = text.replace(/^(plan|treatment|advised)\s*/i, '');
    }

    /* Insert text into the active target textarea */
    if (_state.activeTarget && text) {
      const el = document.getElementById(_state.activeTarget);
      if (el) {
        el.value = el.value ? el.value.trimEnd() + ' ' + text : text;
        el.scrollTop = el.scrollHeight;
      }
    }
  }

  function _switchTarget(targetId) {
    _state.activeTarget = targetId;
    /* Visual feedback — highlight the active textarea */
    ['soapSubjective', 'soapObjective', 'soapPlan'].forEach((id) => {
      const el = document.getElementById(id);
      if (el) el.style.borderColor = id === targetId ? 'var(--color-primary)' : '';
    });
  }

  function start(defaultTargetId) {
    if (!_state.recognition) return;
    _state.activeTarget = defaultTargetId || 'soapSubjective';
    _state.isListening = true;
    try {
      _state.recognition.start();
      _state.onStatusChange({ isListening: true });
    } catch (e) {
      /* Already started */
    }
  }

  function stop() {
    if (!_state.recognition) return;
    _state.isListening = false;
    try { _state.recognition.stop(); } catch (e) { /* ignore */ }
    _state.onStatusChange({ isListening: false });
    /* Remove visual highlights */
    ['soapSubjective', 'soapObjective', 'soapPlan'].forEach((id) => {
      const el = document.getElementById(id);
      if (el) el.style.borderColor = '';
    });
  }

  function toggle(defaultTargetId) {
    if (_state.isListening) { stop(); } else { start(defaultTargetId); }
  }

  function setTarget(targetId) {
    _switchTarget(targetId);
  }

  window.VoiceSOAP = { isSupported, init, start, stop, toggle, setTarget };
})();
