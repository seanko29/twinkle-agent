const assets=window.TWINKLE_ASSETS;
const grid=document.querySelector('#grid'),statusEl=document.querySelector('#status'),groupEl=document.querySelector('#group'),playEl=document.querySelector('#play'),searchEl=document.querySelector('#search');
let mode='recommended',language='ko';
const esc=value=>String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const text=(ko,en)=>language==='en'?en:ko;
const english=value=>value.replaceAll('_',' ').replace(/^./,letter=>letter.toUpperCase());
const title=asset=>text(asset.title_ko,english(asset.action));
const group=asset=>text(asset.group_label,english(asset.group));
const framing={full_body:['전신','Full body'],portrait:['상반신','Portrait'],multi_portrait:['여러 명 · 상반신','Group portrait'],multi_full_body:['여러 명 · 전신','Group, full body'],face_closeup:['얼굴 확대','Close-up'],scene_card:['장면 카드','Scene card']};
function render(){
 const query=searchEl.value.trim().toLowerCase();
 const visible=assets.filter(asset=>(mode==='all'||mode==='recommended'&&asset.recommended||mode==='scene'&&asset.contains_drawn_background)&&(!groupEl.value||asset.group===groupEl.value)&&(!query||(asset.id+' '+asset.title_ko+' '+asset.group_label+' '+asset.action+' '+group(asset)+' '+title(asset)).toLowerCase().includes(query)));
 statusEl.textContent=text(`${visible.length}개 표시 · 원본 240–480px · GIF 39개 · APNG 8개`,`${visible.length} results · 240–480px originals · 39 GIFs · 8 APNGs`);
 grid.innerHTML=visible.map(asset=>`<article class="card"><div class="picture"><img src="${esc(playEl.checked?asset.original_file:asset.preview_file)}" alt="${esc(title(asset))}" loading="lazy" data-fallback="${esc(asset.preview_file)}"></div><div class="body"><div class="topline"><span>${esc(asset.id)} · ${esc(group(asset))}</span>${asset.recommended?`<span class="badge">${text('추천','Pick')}</span>`:''}</div><h2>${esc(title(asset))}</h2><div class="meta">${asset.width} × ${asset.height} · ${asset.frames} ${text('프레임','frames')} · ${(asset.duration_ms/1000).toFixed(2)}${text('초','s')}<br>${esc((framing[asset.framing]||[asset.framing,asset.framing])[language==='en'?1:0])}${asset.contains_text?text(' · 문구 포함',' · Text'):''}${asset.contains_drawn_background?text(' · 그림 배경 포함',' · Drawn background'):text(' · 투명 배경',' · Transparent')}</div><div class="files"><a href="${esc(asset.original_file)}" target="_blank" rel="noopener noreferrer">${text('원본','Original')}</a><a href="${esc(asset.preview_file)}" target="_blank" rel="noopener noreferrer">PNG</a><a href="${esc(asset.sprite_file)}" target="_blank" rel="noopener noreferrer">${text('시트','Sheet')}</a><a href="${esc(asset.animation_file)}" target="_blank" rel="noopener noreferrer">${text('재생정보','Timing')}</a></div><div class="source"><a href="${esc(asset.source_page)}" target="_blank" rel="noopener noreferrer">${text('출처','Source')} ↗</a> · ${esc(asset.pack_title)}</div></div></article>`).join('')||`<div class="empty">${text('조건에 맞는 이미지가 없습니다.','No matching images.')}</div>`;
 grid.querySelectorAll('img').forEach(image=>image.addEventListener('error',()=>{if(image.dataset.fallback){const fallback=image.dataset.fallback;delete image.dataset.fallback;image.src=fallback}}));
}
function setLanguage(value){
 language=value==='en'?'en':'ko';document.documentElement.lang=language;
 document.querySelectorAll('[data-ko][data-en]').forEach(element=>element.textContent=element.dataset[language]);
 document.querySelectorAll('[data-lang]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.lang===language)));
 const selected=groupEl.value;groupEl.innerHTML=`<option value="">${text('모든 모습','All groups')}</option>`;
 for(const key of [...new Set(assets.map(asset=>asset.group))]){const asset=assets.find(asset=>asset.group===key);const option=document.createElement('option');option.value=key;option.textContent=group(asset);groupEl.appendChild(option)}
 groupEl.value=selected;searchEl.placeholder=text('하품, 웃음, 응원…','Search yawns, laugh, cheer…');searchEl.setAttribute('aria-label',text('동작 검색','Search animations'));
 try{localStorage.setItem('twinkle-language',language);const url=new URL(location.href);url.searchParams.set('lang',language);history.replaceState(null,'',url)}catch{}render();
}
document.querySelectorAll('[data-lang]').forEach(button=>button.addEventListener('click',()=>setLanguage(button.dataset.lang)));
document.querySelectorAll('[data-mode]').forEach(button=>button.addEventListener('click',()=>{mode=button.dataset.mode;document.querySelectorAll('[data-mode]').forEach(item=>item.classList.toggle('active',item===button));render()}));
groupEl.addEventListener('change',render);playEl.addEventListener('change',render);searchEl.addEventListener('input',render);document.querySelector('#bg').addEventListener('change',event=>document.body.className=event.target.value);
let initialLanguage=new URLSearchParams(location.search).get('lang');if(!initialLanguage){try{initialLanguage=localStorage.getItem('twinkle-language')}catch{}}setLanguage(initialLanguage||'ko');
