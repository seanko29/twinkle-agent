/* Only button state is inspected. No prompts, responses, page titles or chat URLs leave the tab. */
(function (scope) {
  const selectors = {
    ChatGPT: ['button[data-testid="stop-button"]', 'button[data-testid="stop-generating-button"]'],
    Claude: ['button[data-testid="stop-streaming"]', 'button[data-testid="stop-button"]'],
    Gemini: ['button.stop', 'button.stop-button', 'button[mattooltip="Stop response"]']
  };
  const labels = /^(stop|stop response|stop generating|stop streaming|응답 중지|생성 중지|중지|답변 중지|응답 생성 중지|停止生成|停止回复|停止回應)$/i;
  function provider(host) {
    return host === 'chatgpt.com' ? 'ChatGPT' : host === 'claude.ai' ? 'Claude' : host === 'gemini.google.com' ? 'Gemini' : null;
  }
  function isBusy(doc, service) {
    const visible = button => !button.disabled && button.getClientRects().length > 0 && button.getAttribute('aria-hidden') !== 'true';
    for (const selector of selectors[service] || []) for (const button of doc.querySelectorAll(selector)) if (visible(button)) return true;
    for (const button of doc.querySelectorAll('button[aria-label],button[title]')) {
      const label = (button.getAttribute('aria-label') || button.getAttribute('title') || '').trim();
      if (labels.test(label) && visible(button)) return true;
    }
    return false;
  }
  class CompletionDetector {
    constructor(complete) { this.complete = complete; this.reset(); }
    reset() { this.busySince = null; this.idleSince = null; this.armed = false; this.cancelled = false; }
    cancel() { this.cancelled = true; }
    sample(busy, now) {
      if (busy) {
        this.idleSince = null;
        if (this.busySince === null) this.busySince = now;
        if (now - this.busySince >= 400) this.armed = true;
        return;
      }
      if (this.busySince === null) return;
      if (this.idleSince === null) this.idleSince = now;
      if (now - this.idleSince < 1800) return;
      const notify = this.armed && !this.cancelled; this.reset(); if (notify) this.complete();
    }
  }
  scope.TwinkleCompletion = { provider, isBusy, CompletionDetector };
})(globalThis);
