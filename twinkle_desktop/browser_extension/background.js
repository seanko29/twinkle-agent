const allowed = new Map([['chatgpt.com','ChatGPT'],['claude.ai','Claude'],['gemini.google.com','Gemini']]);
chrome.runtime.onMessage.addListener((request, sender, reply) => {
  let source;
  if (request.type === 'test' && sender.url === chrome.runtime.getURL('popup.html')) source = '브라우저 테스트';
  else if (request.type === 'completed' && sender.tab) {
    try { const url = new URL(sender.url); if (url.protocol === 'https:') source = allowed.get(url.hostname); } catch {}
  }
  if (!source) { reply({ok: false, message: '허용된 AI 사이트가 아닙니다.'}); return false; }
  chrome.storage.local.get({enabled: false}).then(async settings => {
    if (!settings.enabled && request.type !== 'test') return reply({ok: true, message: '알림이 꺼져 있어요.'});
    const payload = {source, title: source === '브라우저 테스트' ? '브라우저 연결 테스트' : source + ' 응답이 끝났어요', message: source === '브라우저 테스트' ? '브라우저에서 캐릭터까지 알림이 도착했어요.' : '열어 둔 AI 탭을 확인해 주세요. 도구 작업 전체가 끝났는지는 직접 확인해 주세요.'};
    let result;
    try { result = await chrome.runtime.sendNativeMessage('com.twinkle.desktop', payload); }
    catch (error) { result = {ok: false, message: '브라우저 연결 설치와 캐릭터 실행을 확인해 주세요. ' + error.message}; }
    await chrome.storage.local.set({lastResult: result.message, lastOk: result.ok, lastTime: new Date().toISOString()});
    reply(result);
  }).catch(error => reply({ok: false, message: error.message}));
  return true;
});
