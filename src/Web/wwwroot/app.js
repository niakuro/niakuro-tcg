const $ = id => document.getElementById(id);
const cookieName = 'niakuro_player_name';
let connection, roomCode = '', state = null;

function getCookie(name) { const raw = document.cookie.split('; ').find(r => r.startsWith(name + '='))?.split('=').slice(1).join('=') || ''; try { return decodeURIComponent(raw); } catch { return raw; } }
function setCookie(name, value) { document.cookie = `${name}=${encodeURIComponent(value)}; max-age=31536000; path=/; samesite=lax`; }

const savedName = getCookie(cookieName);
if (savedName) $('name').value = savedName;

async function connect() {
  if (connection) return;
  connection = new signalR.HubConnectionBuilder().withUrl('/gameHub').withAutomaticReconnect().build();
  connection.on('roomState', s => { state = s; render(s); });
  await connection.start();
}

async function enterLobby(code) {
  roomCode = code;
  $('login').classList.add('hidden'); $('lobby').classList.remove('hidden');
  $('code').textContent = code; $('matchCode').textContent = 'ルーム ' + code;
}

$('create').onclick = async () => {
  const name = $('name').value.trim(); if (!name) return $('loginStatus').textContent = '名前を入力してください。';
  setCookie(cookieName, name);
  try { await connect(); const code = await connection.invoke('CreateRoom', name, $('deck').value); await enterLobby(code); }
  catch (e) { $('loginStatus').textContent = e.message; }
};

$('join').onclick = async () => {
  const name = $('name').value.trim(); const code = $('roomCode').value.trim().toUpperCase(); if (!name) return $('loginStatus').textContent = '名前を入力してください。';
  setCookie(cookieName, name);
  try { await connect(); await connection.invoke('JoinRoom', code, name, $('deck').value); await enterLobby(code); }
  catch (e) { $('loginStatus').textContent = e.message; }
};

$('start').onclick = async () => {
  try { await connection.invoke('StartRoom', roomCode); }
  catch (e) { $('lobbyStatus').textContent = e.message; }
};

async function action(name, value = '') {
  try { await connection.invoke('Action', roomCode, name, value); }
  catch (e) { $('gameStatus').textContent = e.message; }
}

function render(s) {
  if (!s.started) {
    $('players').innerHTML = s.players.map(p => `<div>${escapeHtml(p.name)} — ${deckName(p.deckId)}</div>`).join('');
    $('lobbyStatus').textContent = s.players.length === 1 ? '対戦相手を待っています。' : '対戦相手が入りました。開始できます。';
    return;
  }
  $('lobby').classList.add('hidden'); $('game').classList.remove('hidden');
  $('turnInfo').textContent = s.gameOver ? '対戦終了' : (s.isMyTurn ? 'あなたのターン' : '相手のターン') + ` ／ ${s.globalTurn}ターン目`;
  $('gaugeText').textContent = `戦線ゲージ ${signed(s.gauge)}`;
  $('gaugeMarker').style.left = ((s.gauge + 15) / 30 * 100) + '%';

  $('mePanel').innerHTML = playerInfo('あなた', s.me, true);
  $('opponentPanel').innerHTML = playerInfo('相手', s.opponent, false);
  $('myField').innerHTML = s.me.field.map(unitCard).join('');
  $('opponentField').innerHTML = s.opponent.field.map(unitCard).join('');
  $('hand').innerHTML = s.me.hand.map(card => cardTemplate(card)).join('');
  renderControls(s);
  if (s.gameOver) $('gameStatus').textContent = `勝者：${s.winnerIndex === 0 ? 'プレイヤー1' : 'プレイヤー2'}`;
}

function playerInfo(label, p, me) {
  if (me) return `<b>${label}</b><br>ポイント ${p.points}（基本 ${p.basePoints}）<br>デッキ ${p.deckCount}<br>墓地 ${p.graveyardCount}`;
  return `<b>${label}</b><br>手札 ${p.handCount}<br>デッキ ${p.deckCount}<br>墓地 ${p.graveyardCount}`;
}

function cardTemplate(c) {
  const unit = c.type === 'Unit';
  return `<button class="card ${unit?'unit':''}" data-card="${c.instanceId}" data-type="${c.type}" title="${escapeHtml(c.name)}"><div class="cost">コスト ${c.cost}</div><div class="card-name">${escapeHtml(c.name)}</div><div class="stats">${unit?'ユニット':'戦術'}</div></button>`;
}

function unitCard(u) {
  return `<button class="card unit" data-unit="${u.instanceId}" title="ユニットを選択"><div class="card-name">${escapeHtml(u.name)}</div><div class="stats">${u.attack} / ${u.currentHealth}</div><div>最大 ${u.maxHealth}${u.guard?'／守護':''}</div></button>`;
}

function renderControls(s) {
  const c = $('controls'); c.innerHTML = '';
  if (s.gameOver) return;
  if (s.pendingHaisui != null) {
    c.innerHTML = `<button data-act="haisui" data-val="PushBack">ゲージを押し戻す</button><button data-act="haisui" data-val="DrawCards">4枚ドロー</button><button data-act="haisui" data-val="NextTurnPoints">次ターン＋3ポイント</button>`;
  } else {
    if (!s.me.mulliganUsed) c.insertAdjacentHTML('beforeend','<button data-act="mulligan">マリガン</button>');
    c.insertAdjacentHTML('beforeend','<button data-act="endTurn">ターン終了</button>');
  }
  document.queryAll = c.querySelectorAll.bind(c);
  c.querySelectorAll('[data-act]').forEach(b => b.onclick = () => action(b.dataset.act, b.dataset.val || ''));

  document.querySelectorAll('#hand [data-card]').forEach(card => card.onclick = () => {
    document.querySelectorAll('#hand .selected').forEach(x => x.classList.remove('selected')); card.classList.add('selected');
    $('gameStatus').textContent = card.dataset.type === 'Unit' ? 'ユニットの使用ボタンを押してください。' : '戦術の使用ボタンを押してください。';
    const name = card.dataset.type === 'Unit' ? 'playUnit' : 'playTactic';
    const old = c.querySelector('[data-selected-card]'); if (old) old.remove();
    c.insertAdjacentHTML('afterbegin', `<button data-selected-card data-act="${name}" data-val="${card.dataset.card}">選択カードを使用</button>`);
    c.querySelector('[data-selected-card]').onclick = () => action(name, card.dataset.card);
  });

  let selectedUnit = null;
  document.queryAll('#myField [data-unit]').forEach(btn => btn.onclick = () => {
    document.querySelectorAll('#myField .selected').forEach(x => x.classList.remove('selected')); btn.classList.add('selected'); selectedUnit = btn.dataset.unit;
    const old = c.querySelector('[data-face]'); if (old) old.remove();
    c.insertAdjacentHTML('afterbegin', `<button data-face>顔へ攻撃</button>`);
    c.querySelector('[data-face]').onclick = () => action('attackFace', selectedUnit);
  });
  document.queryAll('#opponentField [data-unit]').forEach(btn => btn.onclick = () => {
    if (!selectedUnit) return;
    action('attackUnit', selectedUnit + '|' + btn.dataset.unit);
  });
}

function signed(n) { return n > 0 ? '+' + n : String(n); }
function deckName(id) { return ({niakuro:'にあくろ × ニトグリ',muscat:'マスカット × ミルキー',coconut:'ココナッツ × やーや'})[id] || id; }
function escapeHtml(s) { return String(s).replace(/[&<>'"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c])); }
