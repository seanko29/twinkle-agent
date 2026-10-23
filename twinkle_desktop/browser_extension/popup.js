const toggle = document.getElementById('enabled'), result = document.getElementById('result');
chrome.storage.local.get({enabled: false, lastResult: '연결 테스트를 눌러 주세요.'}).then(value => { toggle.checked = value.enabled; result.textContent = value.lastResult; });
toggle.addEventListener('change', () => chrome.storage.local.set({enabled: toggle.checked}));
document.getElementById('test').addEventListener('click', async () => {
  result.textContent = '캐릭터에 보내는 중…';
  try { result.textContent = (await chrome.runtime.sendMessage({type: 'test'})).message; }
  catch (error) { result.textContent = error.message; }
});
