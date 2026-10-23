(() => {
  const {provider, isBusy, CompletionDetector} = globalThis.TwinkleCompletion;
  const service = provider(location.hostname); if (!service) return;
  let enabled = true, currentPath = location.pathname;
  const detector = new CompletionDetector(() => {
    chrome.runtime.sendMessage({type: 'completed', source: service}).catch(() => {});
  });
  chrome.storage.local.get({enabled: false}).then(value => { enabled = value.enabled; });
  chrome.storage.onChanged.addListener(changes => {
    if (changes.enabled) { enabled = changes.enabled.newValue; detector.reset(); }
  });
  function sample() {
    if (location.pathname !== currentPath) { currentPath = location.pathname; detector.reset(); }
    if (enabled) detector.sample(isBusy(document, service), performance.now());
    else detector.reset();
  }
  let scheduled = false;
  new MutationObserver(() => {
    if (!scheduled) { scheduled = true; setTimeout(() => { scheduled = false; sample(); }, 100); }
  }).observe(document.documentElement, {subtree: true, childList: true, attributes: true, attributeFilter: ['aria-label','title','disabled','aria-hidden','class','style']});
  // Don't describe a manually stopped response as completed.
  document.addEventListener('click', event => {
    const button = event.target.closest && event.target.closest('button');
    if (button && /^(stop|stop response|stop generating|stop streaming|응답 중지|생성 중지|중지|답변 중지|응답 생성 중지)$/i.test((button.getAttribute('aria-label') || button.getAttribute('title') || '').trim())) detector.cancel();
  }, true);
  setInterval(sample, 500); sample();
})();
