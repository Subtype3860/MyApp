/*!
 * «1000» — интерфейс: стол, карты, торги, модальные окна.
 */
(function (root) {
  'use strict';

  const E = root.ThousandEngine;
  const S = root.ThousandSound;
  const SUITS = E.SUITS;
  const LABEL = E.RANK_LABEL;

  const $ = (id) => document.getElementById(id);

  function fmt(n) {
    const value = Number(n) || 0;
    return (value < 0 ? '−' : '') + Math.abs(value).toLocaleString('ru-RU');
  }

  function delta(n) {
    return (n >= 0 ? '+' : '−') + Math.abs(n).toLocaleString('ru-RU');
  }

  function titleCase(text) {
    return text.charAt(0).toUpperCase() + text.slice(1);
  }

  function escapeHtml(value) {
    return String(value == null ? '' : value).replace(/[&<>"']/g, (char) => {
      if (char === '&') return '&amp;';
      if (char === '<') return '&lt;';
      if (char === '>') return '&gt;';
      if (char === '"') return '&quot;';
      return '&#39;';
    });
  }

  function cardEl(cardId, opts) {
    const options = opts || {};
    const el = document.createElement('div');
    el.className = 'card';
    if (options.tiny) el.classList.add('tiny');
    if (options.mini) el.classList.add('mini');
    if (options.cls) el.classList.add(...options.cls.split(' '));
    if (options.back || !cardId) {
      el.classList.add('back');
      el.setAttribute('aria-hidden', 'true');
      return el;
    }
    const suit = SUITS[cardId[0]];
    const rank = cardId.slice(1);
    if (!suit) return el;
    if (suit.color === 'red') el.classList.add('red');
    if (rank === 'K' || rank === 'Q' || rank === 'J') el.classList.add('face');
    if (rank === 'A') el.classList.add('ace');
    if (rank === '10') el.classList.add('ten');
    if (rank === '10') el.classList.add('rank-ten');
    el.dataset.card = cardId;
    el.title = E.cardLongName(cardId);
    el.setAttribute('aria-label', E.cardLongName(cardId));

    const corner = `<div class="card-corner"><span class="card-rank">${LABEL[rank]}</span><span class="card-suit">${suit.symbol}</span></div>`;
    let center;
    if (rank === 'K' || rank === 'Q' || rank === 'J') {
      center = `<div class="card-center"><span class="face-letter">${LABEL[rank]}</span><span class="face-suit">${suit.symbol}</span></div>`;
    } else {
      center = `<div class="card-center"><span class="big-suit">${suit.symbol}</span>${rank === '10' ? '<span class="ten-mark">10</span>' : ''}</div>`;
    }
    el.innerHTML = `${corner}${center}${corner.replace('card-corner', 'card-corner bottom')}`;
    return el;
  }

  class GameUI {
    constructor(callbacks) {
      this.cb = callbacks || {};
      this.state = null;
      this.prevPhase = null;
      this.selected = [];
      this.discardSwap = false;
      this.bidDraft = 100;
      this.bidKey = '';
      this.handKey = '';
      this.trickKey = '';
      this.prevHand = [];
      this.modalOpen = false;
      this.modalClosable = false;
      this.modalKind = null;
      this.suppressClose = false;
      this.confettiRaf = 0;

      this.el = {
        menuScreen: $('menuScreen'),
        gameScreen: $('gameScreen'),
        menuFan: $('menuFan'),
        menuStats: $('menuStats'),
        playBtn: $('playBtn'),
        onlineBtn: $('onlineBtn'),
        menuRulesBtn: $('menuRulesBtn'),
        menuSoundBtn: $('menuSoundBtn'),
        soundBtn: $('soundBtn'),
        rulesBtn: $('rulesBtn'),
        scoreBtn: $('scoreBtn'),
        menuBtn: $('menuBtn'),
        roomTag: $('roomTag'),
        roundNum: $('roundNum'),
        trumpPill: $('trumpPill'),
        trumpValue: $('trumpValue'),
        contractPill: $('contractPill'),
        contractValue: $('contractValue'),
        seats: [$('seat0'), $('seat1'), $('seat2')],
        trickArea: $('trickArea'),
        tableMessage: $('tableMessage'),
        auctionBubble: $('auctionBubble'),
        talonPile: $('talonPile'),
        feed: $('feed'),
        dock: document.querySelector('.dock'),
        dockTop: document.querySelector('.dock-top'),
        turnBox: document.querySelector('.turn-box'),
        turnDot: $('turnDot'),
        turnText: $('turnText'),
        turnHint: $('turnHint'),
        dockActions: $('dockActions'),
        hand: $('hand'),
        bidPanel: $('bidPanel'),
        bidValue: $('bidValue'),
        bidMax: $('bidMax'),
        bidMinus: $('bidMinus'),
        bidPlus: $('bidPlus'),
        bidSubmit: $('bidSubmit'),
        bidPass: $('bidPass'),
        modalRoot: $('modalRoot'),
        toastRoot: $('toastRoot'),
        confetti: $('confetti'),
      };

      this.bind();
      this.renderMenuFan();
      this.syncSoundButtons();
      this.updateMenuStats();
    }

    bind() {
      this.el.playBtn.addEventListener('click', () => {
        S.init();
        S.play('click');
        if (this.cb.onStart) this.cb.onStart();
      });
      this.el.onlineBtn.addEventListener('click', () => {
        S.init();
        S.play('click');
        if (this.cb.onOnlinePanel) this.cb.onOnlinePanel();
      });
      this.el.menuRulesBtn.addEventListener('click', () => {
        S.play('click');
        this.showRules();
      });
      this.el.rulesBtn.addEventListener('click', () => {
        S.play('click');
        this.showRules();
      });
      this.el.scoreBtn.addEventListener('click', () => {
        S.play('click');
        this.showScores(this.state);
      });
      this.el.menuBtn.addEventListener('click', () => {
        S.play('click');
        this.confirmMenu();
      });

      const toggleSound = () => {
        S.toggle();
        this.syncSoundButtons();
      };
      this.el.menuSoundBtn.addEventListener('click', toggleSound);
      this.el.soundBtn.addEventListener('click', toggleSound);

      this.el.bidMinus.addEventListener('click', () => this.adjustBid(-5));
      this.el.bidPlus.addEventListener('click', () => this.adjustBid(5));
      this.el.bidSubmit.addEventListener('click', () => this.submitBid());
      this.el.bidPass.addEventListener('click', () => {
        S.play('pass');
        if (this.cb.onPass) this.cb.onPass();
      });

      this.el.hand.addEventListener('click', (event) => this.onHandClick(event));

      this.el.modalRoot.addEventListener('click', (event) => {
        if (event.target === this.el.modalRoot && this.modalClosable) this.closeModal();
      });

      document.addEventListener('keydown', (event) => this.onKeyDown(event));
    }

    /* ---------- меню ---------- */

    renderMenuFan() {
      const cards = ['HA', 'HK', 'SQ', 'D10', 'C9'];
      this.el.menuFan.innerHTML = '';
      cards.forEach((id) => {
        const wrapper = document.createElement('div');
        wrapper.className = 'show-card';
        wrapper.appendChild(cardEl(id));
        this.el.menuFan.appendChild(wrapper);
      });
    }

    updateMenuStats(stats) {
      let data = stats;
      if (!data) {
        try {
          data = JSON.parse(root.localStorage.getItem('thousand.stats') || '{}');
        } catch (_) {
          data = {};
        }
      }
      const games = Number(data.games) || 0;
      const wins = Number(data.wins) || 0;
      if (this.el.menuStats) {
        this.el.menuStats.textContent = games ? `Побед: ${wins} · Игр: ${games}` : 'Побед: 0 · Игр: 0';
      }
    }

    syncSoundButtons() {
      const muted = !S.enabled;
      [this.el.menuSoundBtn, this.el.soundBtn].forEach((btn) => {
        if (!btn) return;
        btn.classList.toggle('muted', muted);
        btn.setAttribute('aria-label', muted ? 'Включить звук' : 'Выключить звук');
        btn.title = muted ? 'Включить звук' : 'Выключить звук';
      });
    }

    showMenu() {
      this.closeModal();
      this.el.menuScreen.classList.remove('hidden');
      this.el.gameScreen.classList.add('hidden');
      this.setOnlineRoom(null);
      this.state = null;
      this.resetView();
      this.stopConfetti();
      this.updateMenuStats();
    }

    setOnlineRoom(code) {
      if (!this.el.roomTag) return;
      this.el.roomTag.classList.toggle('hidden', !code);
      this.el.roomTag.textContent = code ? `КОМНАТА ${code}` : '';
    }

    resetView() {
      this.handKey = '';
      this.trickKey = '';
      this.prevHand = [];
      this.selected = [];
      this.bidKey = '';
      this.bidDraft = 100;
      this.discardSwap = false;
      this.prevPhase = null;
    }

    showGame() {
      this.el.menuScreen.classList.add('hidden');
      this.el.gameScreen.classList.remove('hidden');
    }

    /* ---------- онлайн: вход и лобби ---------- */

    showOnlinePanel() {
      let savedName = 'Игрок';
      try {
        savedName = root.localStorage.getItem('thousand.name') || 'Игрок';
      } catch (_) {
        /* ignore */
      }
      this.openModal({
        kind: 'online',
        kicker: 'ОНЛАЙН',
        title: 'Играть по сети',
        closable: true,
        body: `
          <p class="online-lead">Соберите стол из трёх игроков: создайте комнату и отправьте друзьям код
          или войдите по коду товарища.</p>
          <div class="online-form">
            <label class="online-field">
              <span>Ваше имя</span>
              <input id="onlineName" maxlength="16" autocomplete="nickname" spellcheck="false">
            </label>
            <button class="btn btn-primary" id="onlineCreate" type="button">Создать комнату <span class="btn-arrow">→</span></button>
            <div class="online-divider"><span>или</span></div>
            <label class="online-field">
              <span>Код комнаты</span>
              <input id="onlineCode" maxlength="4" placeholder="ABCD" autocomplete="off" spellcheck="false">
            </label>
            <button class="btn btn-ghost" id="onlineJoin" type="button">Войти по коду</button>
          </div>
          <div class="online-status" id="onlineStatus"></div>
          <p class="online-note">Игра идёт через этот же сервер. Для игры по локальной сети откройте
          адрес сервера на устройстве друга — <b>http://${escapeHtml(root.location.host || '127.0.0.1:8125')}</b>.</p>
        `,
      });
      const nameInput = document.getElementById('onlineName');
      const codeInput = document.getElementById('onlineCode');
      const createBtn = document.getElementById('onlineCreate');
      const joinBtn = document.getElementById('onlineJoin');
      const status = document.getElementById('onlineStatus');
      if (nameInput) nameInput.value = savedName;
      if (codeInput) {
        codeInput.addEventListener('input', () => {
          codeInput.value = codeInput.value.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 4);
        });
        codeInput.addEventListener('keydown', (event) => {
          if (event.key === 'Enter') {
            event.preventDefault();
            if (joinBtn) joinBtn.click();
          }
        });
      }
      if (nameInput) {
        nameInput.addEventListener('keydown', (event) => {
          if (event.key === 'Enter' && createBtn) {
            event.preventDefault();
            createBtn.click();
          }
        });
      }
      this.onlineBusy = false;
      const submit = (mode) => {
        if (this.onlineBusy) return;
        const name = String(nameInput ? nameInput.value : '').trim().slice(0, 16);
        if (name.length < 2) {
          this.toast('Имя должно быть не короче двух символов');
          if (nameInput) nameInput.focus();
          return;
        }
        try {
          root.localStorage.setItem('thousand.name', name);
        } catch (_) {
          /* ignore */
        }
        if (mode === 'create') {
          this.setOnlineBusy(true);
          if (this.cb.onOnlineCreate) this.cb.onOnlineCreate(name);
          return;
        }
        const code = String(codeInput ? codeInput.value : '').toUpperCase().replace(/[^A-Z0-9]/g, '');
        if (code.length < 4) {
          this.toast('Введите четырёхзначный код комнаты');
          if (codeInput) codeInput.focus();
          return;
        }
        this.setOnlineBusy(true);
        if (this.cb.onOnlineJoin) this.cb.onOnlineJoin(code, name);
      };
      if (createBtn) createBtn.addEventListener('click', () => submit('create'));
      if (joinBtn) joinBtn.addEventListener('click', () => submit('join'));
      if (status) status.textContent = '';
      if (nameInput) {
        setTimeout(() => {
          try {
            nameInput.focus();
            nameInput.select();
          } catch (_) {
            /* ignore */
          }
        }, 30);
      }
    }

    setOnlineBusy(busy) {
      this.onlineBusy = Boolean(busy);
      const createBtn = document.getElementById('onlineCreate');
      const joinBtn = document.getElementById('onlineJoin');
      const status = document.getElementById('onlineStatus');
      if (createBtn) createBtn.disabled = this.onlineBusy;
      if (joinBtn) joinBtn.disabled = this.onlineBusy;
      if (status) {
        status.classList.remove('error');
        if (this.onlineBusy) status.textContent = 'Подключаемся к серверу…';
      }
    }

    onlineError(message) {
      this.setOnlineBusy(false);
      const status = document.getElementById('onlineStatus');
      if (status) {
        status.classList.add('error');
        status.textContent = message;
      }
      this.toast(message, 3600);
    }

    showLobby(room) {
      if (!room) return;
      this.lobbyRoom = room;
      if (this.modalKind !== 'lobby') {
        this.openModal({
          kind: 'lobby',
          kicker: 'ОНЛАЙН',
          title: `Лобби ${room.code}`,
          body: '<div id="lobbyContent"></div>',
          closable: false,
        });
      }
      const content = document.getElementById('lobbyContent');
      if (!content) return;
      const me = room.players[0];
      const isHost = Boolean(me && me.isHost);
      const seats = room.players
        .map((player, index) => {
          if (!player) {
            return `
              <div class="lobby-seat empty">
                <div class="lobby-avatar">+</div>
                <div class="lobby-seat-copy"><b>Свободно</b><span>Ждём игрока</span></div>
              </div>
            `;
          }
          const initial = String(player.name || '?').trim().charAt(0).toUpperCase() || '?';
          return `
            <div class="lobby-seat ${player.isYou ? 'you' : ''} ${player.ready ? 'ready' : ''}">
              <div class="lobby-avatar">${escapeHtml(initial)}</div>
              <div class="lobby-seat-copy">
                <b>${escapeHtml(player.name)}${player.isHost ? '<span class="host-tag">хозяин</span>' : ''}</b>
                <span>${player.ready ? 'готов' : 'не готов'}${player.isBot ? ' · бот' : ''}</span>
              </div>
              ${player.isYou ? '<span class="you-tag">вы</span>' : ''}
            </div>
          `;
        })
        .join('');

      content.innerHTML = `
        <div class="lobby-code">
          <div>
            <span class="lobby-code-label">КОД КОМНАТЫ</span>
            <strong>${escapeHtml(room.code)}</strong>
          </div>
          <button class="btn btn-ghost lobby-copy" id="lobbyCopy" type="button">Копировать</button>
        </div>
        <div class="lobby-seats">${seats}</div>
        <div class="lobby-progress">${room.filled} / ${room.max} игроков · готовы ${room.readyCount}</div>
        <div class="lobby-actions">
          <button class="btn btn-ghost" id="lobbyReady" type="button">${me && me.ready ? 'Не готов' : 'Готов'}</button>
          ${
            isHost
              ? `<button class="btn btn-primary" id="lobbyStart" type="button" ${room.canStart ? '' : 'disabled'}>Начать игру</button>`
              : ''
          }
          <button class="btn btn-ghost" id="lobbyLeave" type="button">Выйти</button>
        </div>
        <p class="online-note">${
          isHost
            ? room.canStart
              ? 'Все готовы — можно начинать.'
              : 'Дождитесь третьего игрока и отметьте готовность всех.'
            : 'Ждём, когда хозяин комнаты начнёт игру.'
        }</p>
      `;

      const copyBtn = document.getElementById('lobbyCopy');
      if (copyBtn) {
        copyBtn.addEventListener('click', async () => {
          try {
            await navigator.clipboard.writeText(room.code);
            this.toast('Код комнаты скопирован');
          } catch (_) {
            this.toast(`Код комнаты: ${room.code}`);
          }
        });
      }
      const readyBtn = document.getElementById('lobbyReady');
      if (readyBtn) {
        readyBtn.addEventListener('click', () => {
          if (this.cb.onOnlineReady) this.cb.onOnlineReady(!(me && me.ready));
        });
      }
      const startBtn = document.getElementById('lobbyStart');
      if (startBtn) {
        startBtn.addEventListener('click', () => {
          if (startBtn.disabled) return;
          startBtn.disabled = true;
          startBtn.textContent = 'Начинаем…';
          if (this.cb.onOnlineStart) this.cb.onOnlineStart();
        });
      }
      const leaveBtn = document.getElementById('lobbyLeave');
      if (leaveBtn) {
        leaveBtn.addEventListener('click', () => {
          if (this.cb.onOnlineLeave) this.cb.onOnlineLeave();
        });
      }
    }

    /* ---------- общий рендер ---------- */

    render(state) {
      if (!state) return;
      const prev = this.state;
      this.prevPhase = prev ? prev.phase : null;
      if (prev && prev.phase === 'discard' && state.phase !== 'discard') this.selected = [];
      if (state.phase === 'discard' && (!prev || prev.phase !== 'discard')) {
        this.selected = [];
        this.discardSwap = false;
      }
      this.state = state;
      this.renderTopbar();
      this.renderSeats();
      this.renderTrick();
      this.renderHand();
      this.renderFeed();
      this.renderTalon();
      this.renderAuctionBubble();
      this.renderTableMessage();
      this.renderDock();
    }

    renderTopbar() {
      const s = this.state;
      this.el.roundNum.textContent = s.round;
      const trump = s.trump ? SUITS[s.trump] : null;
      this.el.trumpPill.classList.toggle('red', Boolean(trump && trump.color === 'red'));
      this.el.trumpPill.classList.toggle('active', Boolean(trump));
      this.el.trumpValue.textContent = trump ? trump.symbol : '—';
      const showContract = s.contract > 0 && s.bidder != null && s.phase !== 'bidding';
      this.el.contractPill.classList.toggle('hidden', !showContract);
      this.el.contractValue.textContent = showContract ? s.contract : '—';
    }

    renderSeats() {
      const s = this.state;
      for (let p = 0; p < 3; p += 1) {
        const seat = this.el.seats[p];
        if (!seat) continue;
        const pos = p === 0 ? 'seat-you' : p === 1 ? 'seat-left' : 'seat-right';
        const active =
          (s.phase === 'bidding' && s.auction && s.auction.turn === p) ||
          (s.phase === 'playing' && s.turn === p && !s.trickComplete);
        const winner = s.trickComplete && s.lastTrick && s.lastTrick.winner === p;
        seat.className = `seat ${pos}${active ? ' active' : ''}${winner ? ' winner' : ''}`;

        const player = s.players[p];
        const cards = s.hands[p].length;
        const dealer = s.dealer === p ? '<span class="dealer-chip">сдаёт</span>' : '';
        const bolts = [0, 1, 2]
          .map((i) => `<i class="${i < player.bolts ? 'on' : ''}"></i>`)
          .join('');
        const avatarClass = p === 0 ? 'avatar-you' : p === 1 ? 'avatar-voron' : 'avatar-lisa';
        const initial = p === 0 ? 'В' : p === 1 ? 'В' : 'Л';

        let bid = '';
        if (s.phase === 'bidding' && s.auction) {
          const last = s.auction.history[s.auction.history.length - 1];
          if (last && last.player === p) {
            bid = `<div class="seat-bid ${last.type === 'pass' ? 'pass' : ''}">${last.type === 'pass' ? 'пас' : last.amount}</div>`;
          } else if (s.auction.highBidder === p && s.auction.high > 0) {
            bid = `<div class="seat-bid">${s.auction.high}</div>`;
          }
        } else if (s.bidder === p && s.contract > 0) {
          bid = `<div class="seat-bid">${s.contract}</div>`;
        }

        const marriages = s.marriages
          .filter((m) => m.player === p)
          .map((m) => {
            const suit = SUITS[m.suit];
            return `<span class="marriage-chip ${suit.color === 'red' ? 'red' : ''}">${suit.symbol} +${m.value}</span>`;
          })
          .join('');

        seat.innerHTML = `
          ${bid}
          <div class="seat-avatar ${avatarClass}">${initial}</div>
          <div class="seat-info">
            <div class="seat-name">${player.name}${dealer}</div>
            <div class="seat-score"><b class="${player.score < 0 ? 'negative' : ''}">${fmt(player.score)}</b><span>очков</span></div>
            <div class="seat-bolts">${bolts}</div>
          </div>
          ${cards > 0 && s.phase !== 'scoring' && s.phase !== 'gameover' ? `<div class="seat-cards">${cards}</div>` : ''}
          ${marriages ? `<div class="seat-marriages">${marriages}</div>` : ''}
        `;
      }
    }

    renderTrick() {
      const s = this.state;
      const key = s.trick.map((t) => `${t.player}:${t.card}`).join(',') + '|' + (s.trickComplete ? 'done' : 'live');
      if (key !== this.trickKey) {
        this.trickKey = key;
        this.el.trickArea.classList.remove('collecting');
        this.el.trickArea.querySelectorAll('.trick-slot').forEach((slot) => {
          slot.innerHTML = '';
        });
        for (const play of s.trick) {
          const slot = this.el.trickArea.querySelector(`.trick-slot[data-seat="${play.player}"]`);
          if (slot) slot.appendChild(cardEl(play.card));
        }
      }
    }

    renderHand() {
      const s = this.state;
      const hand = s.hands[0].slice();
      hand.sort((a, b) => {
        const sa = SUITS[a[0]].order;
        const sb = SUITS[b[0]].order;
        if (sa !== sb) return sa - sb;
        return E.cardPower(b) - E.cardPower(a);
      });
      const key = hand.join(',') + '|' + s.phase + '|' + s.bidder;
      if (key !== this.handKey) {
        const prevCount = this.prevHand.length;
        const animate = prevCount === 0 || hand.length > prevCount || s.phase === 'discard';
        this.handKey = key;
        this.prevHand = hand.slice();
        this.el.hand.innerHTML = '';
        const n = hand.length;
        hand.forEach((card, i) => {
          const wrapper = document.createElement('div');
          wrapper.className = `hand-card${animate ? '' : ' no-anim'}`;
          wrapper.dataset.card = card;
          const angle = n <= 1 ? 0 : (i - (n - 1) / 2) * Math.min(2.8, 19 / Math.max(1, n - 1));
          wrapper.style.setProperty('--rot', `${angle.toFixed(2)}deg`);
          wrapper.style.setProperty('--i', i);
          if (!animate) wrapper.style.animation = 'none';
          wrapper.appendChild(cardEl(card));
          this.el.hand.appendChild(wrapper);
        });
      }
      this.updateHandClasses();
    }

    updateHandClasses() {
      const s = this.state;
      const isDiscard = s.phase === 'discard' && s.bidder === 0;
      const isPlay = s.phase === 'playing' && s.turn === 0 && !s.trickComplete;
      const legal = isPlay ? new Set(E.legalCards(s, 0)) : null;
      const recipients = this.discardRecipients();
      this.el.hand.querySelectorAll('.hand-card').forEach((wrapper) => {
        const card = wrapper.dataset.card;
        wrapper.classList.remove('legal', 'illegal', 'selected', 'selectable', 'disabled', 'trump');
        const badge = wrapper.querySelector('.card-badge');
        if (badge) badge.remove();
        const trumpMark = wrapper.querySelector('.trump-mark');
        if (trumpMark) trumpMark.remove();
        if (s.trump && card[0] === s.trump) {
          wrapper.classList.add('trump');
          const mark = document.createElement('span');
          mark.className = 'trump-mark';
          mark.textContent = SUITS[s.trump].symbol;
          wrapper.appendChild(mark);
        }
        if (isDiscard) {
          wrapper.classList.add('selectable');
          const index = this.selected.indexOf(card);
          if (index >= 0) {
            wrapper.classList.add('selected');
            const to = recipients[index];
            const chip = document.createElement('span');
            chip.className = 'card-badge';
            chip.textContent = `→ ${s.players[to].name}`;
            wrapper.appendChild(chip);
          }
        } else if (isPlay) {
          if (legal.has(card)) wrapper.classList.add('legal');
          else wrapper.classList.add('illegal');
        } else {
          wrapper.classList.add('disabled');
        }
      });
    }

    onHandClick(event) {
      const wrapper = event.target.closest('.hand-card');
      if (!wrapper || !this.state) return;
      const card = wrapper.dataset.card;
      const s = this.state;

      if (s.phase === 'discard' && s.bidder === 0) {
        // После смены состояния в выборе не должно оставаться карт, которых уже нет на руке.
        const hand = s.hands[0] || [];
        this.selected = this.selected.filter((id) => hand.includes(id));
        if (!hand.includes(card)) return;
        const index = this.selected.indexOf(card);
        if (index >= 0) {
          this.selected.splice(index, 1);
          S.play('click');
          this.updateHandClasses();
          this.renderDock();
          return;
        }
        // Выбор третьей карты заменяет самую раннюю — без ошибки «ровно две».
        if (this.selected.length >= 2) this.selected.shift();
        this.selected.push(card);
        S.play('click');
        this.updateHandClasses();
        this.renderDock();
        return;
      }

      if (s.phase === 'playing' && s.turn === 0 && !s.trickComplete) {
        if (!E.isLegal(s, 0, card)) {
          this.toast(s.pendingMarriage ? 'Сейчас нужен король или дама марьяжа' : 'Этой картой ходить нельзя');
          S.play('warn');
          return;
        }
        if (this.cb.onCard) this.cb.onCard(card);
      }
    }

    renderFeed() {
      const s = this.state;
      const items = s.log.slice(-8);
      this.el.feed.innerHTML = '';
      items.forEach((item) => {
        const row = document.createElement('div');
        row.className = `feed-item kind-${item.kind || 'info'}`;
        const span = document.createElement('span');
        span.textContent = item.text;
        row.appendChild(span);
        this.el.feed.appendChild(row);
      });
    }

    renderTalon() {
      const s = this.state;
      const visible = (s.phase === 'talon' || s.phase === 'discard') && s.talon.length > 0;
      this.el.talonPile.classList.toggle('visible', visible);
      this.el.talonPile.innerHTML = '';
      if (!visible) return;
      const faceUp = s.bidder === 0 || s.talonPublic;
      for (const card of s.talon) {
        this.el.talonPile.appendChild(cardEl(faceUp ? card : null, { back: !faceUp, tiny: true }));
      }
    }

    renderAuctionBubble() {
      const s = this.state;
      const visible = s.phase === 'bidding' && s.auction;
      this.el.auctionBubble.classList.toggle('visible', visible);
      if (!visible) return;
      this.el.auctionBubble.textContent = s.auction.high > 0 ? `СТАВКА ${s.auction.high}` : 'ТОРГИ';
    }

    renderTableMessage() {
      const s = this.state;
      let text = '';
      if (s.phase === 'bidding' && s.auction) {
        const turnName = s.auction.turn === 0 ? 'ваш ход' : `ход ${s.players[s.auction.turn].name}`;
        text = s.auction.high > 0 ? `Ставка ${s.auction.high} · ${turnName}` : `Торги · ${turnName}`;
      } else if (s.phase === 'talon') {
        text = s.talonPublic ? 'Прикуп открыт' : 'Прикуп закрыт · игра на сотне';
      } else if (s.phase === 'discard') {
        text = `Снос · ${s.players[s.bidder].name}`;
      } else if (s.phase === 'playing') {
        if (s.trickComplete && s.lastTrick) {
          text = `Взятка ${s.lastTrick.points} · ${s.players[s.lastTrick.winner].name}`;
        } else if (s.trick.length) {
          text = s.turn === 0 ? 'Ваш ход' : `Ход · ${s.players[s.turn].name}`;
        } else {
          text = s.turn === 0 ? 'Ваш ход' : `Ходит ${s.players[s.turn].name}`;
        }
      } else if (s.phase === 'scoring') {
        text = `Кон ${s.round} завершён`;
      }
      this.el.tableMessage.textContent = text;
    }

    renderDock() {
      const s = this.state;
      const biddingHuman = s.phase === 'bidding' && s.auction && s.auction.turn === 0;
      this.el.bidPanel.classList.add('hidden');
      this.el.turnBox.classList.remove('hidden');
      this.el.dockActions.innerHTML = '';

      if (biddingHuman) {
        this.el.turnBox.classList.add('hidden');
        this.el.bidPanel.classList.remove('hidden');
        this.updateBidPanel();
        const { min, max } = this.bidBounds();
        const hint =
          min > max
            ? 'Ставка недоступна — можно только пасовать'
            : s.auction.high === 0
              ? 'Первая рука обязана заявить минимум 100'
              : 'Выберите ставку и заявите её';
        this.setTurn('Ваши торги', hint, true);
        return;
      }

      let title = 'Ожидание';
      let hint = '';
      let yourTurn = false;

      if (s.phase === 'bidding' && s.auction) {
        title = `${s.players[s.auction.turn].name} торгуется`;
        hint = s.auction.high > 0 ? `Текущая ставка — ${s.auction.high}` : 'Первая рука обязана заявить 100';
      } else if (s.phase === 'talon') {
        title = s.bidder === 0 ? 'Ваш прикуп' : `${s.players[s.bidder].name} забирает прикуп`;
        hint = s.talonPublic ? 'Прикуп открыт для всех' : 'Прикуп закрыт: игра на сотне';
        if (s.bidder === 0) {
          yourTurn = true;
          this.addAction('Забрать прикуп', 'primary', () => {
            if (this.cb.onTakeTalon) this.cb.onTakeTalon();
          });
        }
      } else if (s.phase === 'discard') {
        if (s.bidder === 0) {
          yourTurn = true;
          title = 'Ваш снос';
          hint = this.selected.length
            ? `Выбрано ${this.selected.length} из 2 · по одной карте соперникам`
            : 'Выберите две карты — по одной Ворону и Лисе';
          if (this.selected.length === 2) {
            this.addAction('Поменять', 'ghost', () => {
              this.discardSwap = !this.discardSwap;
              S.play('click');
              this.updateHandClasses();
              this.renderDock();
            });
          }
          this.addAction(`Отдать${this.selected.length ? ` (${this.selected.length})` : ''}`, 'primary', () => {
            if (this.selected.length !== 2) {
              this.toast('Выберите две карты');
              return;
            }
            const recipients = this.discardRecipients();
            if (this.cb.onDiscard) this.cb.onDiscard(this.selected.slice(), recipients);
          }, this.selected.length !== 2);
        } else {
          title = `${s.players[s.bidder].name} выбирает снос`;
          hint = 'Карты отдаются в закрытую';
        }
      } else if (s.phase === 'playing') {
        if (s.trickComplete && s.lastTrick) {
          title =
            s.lastTrick.winner === 0
              ? 'Взятку берёте вы'
              : `Взятку берёт ${s.players[s.lastTrick.winner].name}`;
          hint = `${s.lastTrick.points} ${this.pointsWord(s.lastTrick.points)}`;
        } else if (s.turn === 0) {
          yourTurn = true;
          title = 'Ваш ход';
          const pending = s.pendingMarriage && s.pendingMarriage.player === 0 ? s.pendingMarriage : null;
          if (pending) {
            hint = `Заходите королём или дамой ${SUITS[pending.suit].name}`;
            this.addAction(`Марьяж ${SUITS[pending.suit].symbol} +${pending.value}`, 'ghost', () => {}, true);
            this.addAction('Отменить', 'ghost', () => {
              if (this.cb.onCancelMarriage) this.cb.onCancelMarriage();
            });
          } else {
            hint = s.trick.length
              ? 'Следуйте в масть или бейте козырем'
              : s.trickNumber >= 1
                ? 'Можно объявить марьяж и зайти'
                : 'Заходите любой картой';
            if (!s.trick.length && s.trickNumber >= 1) {
              const ready = E.marriageSuits(s.hands[0]).filter((suit) => !s.declared[0].includes(suit));
              ready.forEach((suit) => {
                const info = SUITS[suit];
                this.addAction(`Марьяж ${info.symbol} +${info.marriage}`, 'ghost', () => {
                  if (this.cb.onMarriage) this.cb.onMarriage(suit);
                });
              });
            }
          }
        } else {
          title = `Ходит ${s.players[s.turn].name}`;
          hint = s.trick.length ? 'Взятка разыгрывается' : 'Выберите момент';
        }
      } else if (s.phase === 'scoring') {
        title = 'Кон завершён';
        hint = 'Считаем очки';
      } else if (s.phase === 'gameover') {
        title = 'Партия завершена';
        hint = 'Итоги на столе';
      }

      this.setTurn(title, hint, yourTurn);
    }

    setTurn(title, hint, yourTurn) {
      this.el.turnText.textContent = title;
      this.el.turnHint.textContent = hint || '';
      this.el.turnDot.classList.toggle('waiting', !yourTurn);
    }

    addAction(label, style, onClick, disabled) {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = `btn ${style === 'primary' ? 'btn-primary' : 'btn-ghost'}`;
      btn.textContent = label;
      btn.disabled = Boolean(disabled);
      if (disabled) btn.style.opacity = '0.4';
      btn.addEventListener('click', () => {
        if (btn.disabled) return;
        S.play('click');
        onClick();
      });
      this.el.dockActions.appendChild(btn);
      return btn;
    }

    pointsWord(n) {
      const mod10 = n % 10;
      const mod100 = n % 100;
      if (mod10 === 1 && mod100 !== 11) return 'очко';
      if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return 'очка';
      return 'очков';
    }

    discardRecipients() {
      const first = E.next(0);
      const second = E.next(first);
      return this.discardSwap ? [second, first] : [first, second];
    }

    /* ---------- торги ---------- */

    bidBounds() {
      const s = this.state;
      const min = s.auction && s.auction.high > 0 ? s.auction.high + 5 : 100;
      const max = E.maxBidFor(s, 0);
      return { min, max };
    }

    updateBidPanel() {
      const s = this.state;
      if (!s.auction) return;
      const { min, max } = this.bidBounds();
      const key = `${s.round}:${s.auction.high}:${s.auction.turn}`;
      if (key !== this.bidKey) {
        this.bidKey = key;
        this.bidDraft = min;
      }
      if (min > max) {
        this.el.bidValue.textContent = '—';
        this.el.bidMax.textContent = `макс. ${max}`;
        this.el.bidMinus.disabled = true;
        this.el.bidPlus.disabled = true;
        this.el.bidSubmit.disabled = true;
        this.el.bidPass.disabled = false;
        this.el.bidPass.title = 'Пас';
        return;
      }
      this.bidDraft = Math.max(min, Math.min(max, Math.round(this.bidDraft / 5) * 5));
      this.el.bidValue.textContent = this.bidDraft;
      this.el.bidMax.textContent = `макс. ${max}`;
      this.el.bidMinus.disabled = this.bidDraft <= min;
      this.el.bidPlus.disabled = this.bidDraft >= max;
      this.el.bidSubmit.disabled = this.bidDraft < min || this.bidDraft > max;
      this.el.bidPass.disabled = s.auction.high === 0;
      this.el.bidPass.title = s.auction.high === 0 ? 'Первая рука обязана заявить 100' : 'Пас';
    }

    adjustBid(deltaValue) {
      const { min, max } = this.bidBounds();
      if (min > max) return;
      this.bidDraft = Math.max(min, Math.min(max, this.bidDraft + deltaValue));
      this.el.bidValue.textContent = this.bidDraft;
      this.el.bidMinus.disabled = this.bidDraft <= min;
      this.el.bidPlus.disabled = this.bidDraft >= max;
      this.el.bidSubmit.disabled = false;
      S.play('click');
    }

    submitBid() {
      const { min, max } = this.bidBounds();
      if (min > max) {
        this.toast('Ваша максимальная ставка ниже текущей — остаётся пас');
        return;
      }
      if (this.bidDraft < min || this.bidDraft > max) {
        this.toast(`Ставка должна быть от ${min} до ${max}`);
        return;
      }
      S.play('bid');
      if (this.cb.onBid) this.cb.onBid(this.bidDraft);
    }

    /* ---------- модальные окна ---------- */

    openModal(config) {
      this.suppressClose = true;
      this.closeModal();
      this.suppressClose = false;
      this.modalOpen = true;
      this.modalClosable = Boolean(config.closable);
      this.modalKind = config.kind || 'generic';
      this.el.modalRoot.classList.remove('hidden');
      const modal = document.createElement('div');
      modal.className = `modal${config.wide ? ' wide' : ''} modal-${config.kind || 'generic'}`;
      const closeBtn = config.closable
        ? '<button class="modal-close" type="button" aria-label="Закрыть">×</button>'
        : '';
      modal.innerHTML = `
        <div class="modal-head">
          <div>
            ${config.kicker ? `<div class="modal-kicker">${config.kicker}</div>` : ''}
            <h2 class="modal-title">${config.title || ''}</h2>
          </div>
          ${closeBtn}
        </div>
        <div class="modal-body">${config.body || ''}</div>
        <div class="modal-actions"></div>
      `;
      const actions = modal.querySelector('.modal-actions');
      (config.actions || []).forEach((action) => {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = `btn ${action.style === 'primary' ? 'btn-primary' : 'btn-ghost'}`;
        btn.textContent = action.label;
        if (action.disabled) {
          btn.disabled = true;
          btn.style.opacity = '0.4';
        }
        btn.addEventListener('click', () => {
          S.play('click');
          if (action.close !== false) this.closeModal();
          if (action.onClick) action.onClick();
        });
        actions.appendChild(btn);
      });
      if (!actions.children.length) actions.remove();
      const close = modal.querySelector('.modal-close');
      if (close) close.addEventListener('click', () => this.closeModal());
      this.el.modalRoot.appendChild(modal);
      if (document.activeElement && document.activeElement.blur) document.activeElement.blur();
    }

    closeModal() {
      if (!this.modalOpen && this.el.modalRoot.classList.contains('hidden')) return;
      const hadModal = this.modalOpen;
      this.modalOpen = false;
      this.modalClosable = false;
      this.modalKind = null;
      this.el.modalRoot.classList.add('hidden');
      this.el.modalRoot.innerHTML = '';
      if (hadModal && !this.suppressClose && this.cb.onModalClose) this.cb.onModalClose();
    }

    closeModalIfKind(kind) {
      if (this.modalKind === kind) this.closeModal();
    }

    showRules() {
      const body = `
        <p>«Тысяча» — русская взятковая игра для трёх игроков. Цель — первым набрать
        <b>1000 очков</b>. В колоде 24 карты: девятки, валеты, дамы, короли, десятки и тузы.</p>

        <div class="rule-grid">
          <div class="rule-card"><b>ТУЗ · 11</b><span>Старшая карта. Десятка — 10, король — 4, дама — 3, валет — 2, девятка — 0.</span></div>
          <div class="rule-card"><b>МАРЬЯЖ</b><span>Король и дама одной масти. Черви — 100, бубны — 80, трефы — 60, пики — 40.</span></div>
          <div class="rule-card"><b>24 КАРТЫ</b><span>По 7 карт игрокам и 3 в прикуп. После сноса у всех по 8 карт — 8 взяток.</span></div>
          <div class="rule-card"><b>3 БОЛТА</b><span>Кон без единой взятки — болт. Три болта списывают 120 очков.</span></div>
        </div>

        <h3>Раздача и торги</h3>
        <p>Каждый получает 7 карт, три карты лежат в прикупе. Торги начинает игрок слева от сдающего:
        он обязан заявить минимум <b>100</b> и не может пасовать на первом круге. Ставки кратны 5.
        Остальные могут пасовать; пас выводит из торгов. Победитель забирает прикуп.</p>
        <p>Максимальная ставка — <b>120 плюс стоимость всех марьяжей на руке</b>. Без марьяжа нельзя
        заявлять больше 120: с пиковым марьяжем максимум 160, с трефовым — 180, с бубновым — 200,
        с червовым — 220.</p>

        <h3>Прикуп и снос</h3>
        <p>Победитель торгов показывает прикуп (если игра шла не «на сотне») и добавляет три карты
        к своим семи. Затем он отдаёт <b>по одной карте каждому сопернику</b> — это снос. У всех
        остаётся по 8 карт, и разыгрываются 8 взяток. Снос идёт в закрытую: соперник видит только
        полученную карту.</p>

        <h3>Марьяж и козырь</h3>
        <p>Марьяж объявляют, когда вы заходите, начиная со второго хода. Для этого нужно зайти
        королём или дамой этой масти. Первый марьяж назначает козырь; следующий может его сменить.
        Очки за марьяж прибавляются к очкам взяток, если вы взяли хотя бы одну взятку.</p>

        <h3>Взятки</h3>
        <ul>
          <li>Заходить можно любой картой.</li>
          <li>Нужно отвечать в масть; если её нет — обязан играть козырем.</li>
          <li>Если козыря нет — можно сбросить любую карту.</li>
          <li>Взятку берёт старший козырь, а если козырей нет — старшая карта заходной масти.</li>
          <li>Перебивать козырем не обязательно, если козырь уже на столе.</li>
        </ul>

        <h3>Подсчёт</h3>
        <p>Игрок, выигравший торги, обязан собрать заказанную сумму с учётом марьяжей. Собрал —
        получает ровно свой заказ; не собрал — теряет его. Остальные получают округлённые очки
        взяток плюс марьяжи. Очки округляются до 5: 17 → 15, 18 → 20.</p>
        <p>Игрок без единой взятки получает болт. Третий болт — минус 120 и сброс счётчика.
        Партия идёт до 1000 очков; при равенстве играется ещё кон.</p>

        <h3>Управление</h3>
        <p>В торгах задайте ставку кнопками «−» и «+» и нажмите «Заявить». В розыгрыше просто
        нажимайте на карту. Марьяж объявляется кнопкой над рукой, после этого нужно зайти королём
        или дамой выбранной масти.</p>
      `;
      this.openModal({
        kind: 'rules',
        kicker: 'ПРАВИЛА',
        title: 'Как играть в «Тысячу»',
        body,
        wide: true,
        closable: true,
        actions: [{ label: 'Понятно', style: 'primary' }],
      });
    }

    showScores(state) {
      if (!state) {
        this.toast('Сначала начните партию');
        return;
      }
      const rows = state.players
        .slice()
        .sort((a, b) => b.score - a.score)
        .map((p, index) => {
          const isBidder = state.bidder === p.id;
          return `
            <tr class="${p.id === 0 ? 'is-you' : ''} ${isBidder ? 'is-bidder' : ''}">
              <td><div class="name-cell"><span class="table-avatar">${p.id === 0 ? 'В' : p.id === 1 ? 'В' : 'Л'}</span>${p.name}</div></td>
              <td>${index + 1}</td>
              <td class="score-cell">${fmt(p.score)}</td>
              <td>${p.bolts}</td>
            </tr>
          `;
        })
        .join('');
      const status = state.bidder != null ? `${state.players[state.bidder].name} · заказ ${state.contract}` : 'Торги не завершены';
      this.openModal({
        kind: 'scores',
        kicker: `КОН ${state.round}`,
        title: 'Счёт за столом',
        body: `
          <div class="result-summary">
            <span class="result-summary-label">Текущий кон</span>
            <span class="result-summary-value">${status}</span>
          </div>
          <table class="result-table">
            <thead><tr><th>Игрок</th><th>Место</th><th>Очки</th><th>Болты</th></tr></thead>
            <tbody>${rows}</tbody>
          </table>
          <p style="margin-top:14px;color:var(--muted-2);font-size:11px">Игра идёт до 1000 очков. Три болта — минус 120.</p>
        `,
        closable: true,
        actions: [{ label: 'Закрыть', style: 'ghost' }],
      });
    }

    showTalon(state) {
      if (!state || state.bidder !== 0) return;
      const cards = state.talon.map((c) => cardEl(c)).map((el) => el.outerHTML).join('');
      this.openModal({
        kind: 'talon',
        kicker: `КОН ${state.round}`,
        title: 'Ваш прикуп',
        body: `
          <p>Три карты прикупа уже у вас. Заберите их и отдайте по одной карте Ворону и Лисе —
          так у всех будет по восемь карт.</p>
          <div class="talon-reveal">${cards}</div>
          ${state.talonPublic ? '' : '<p class="accent">Игра «на сотне»: соперники прикуп не видят.</p>'}
        `,
        closable: false,
        actions: [
          {
            label: 'Забрать прикуп',
            style: 'primary',
            onClick: () => {
              if (this.cb.onTakeTalon) this.cb.onTakeTalon();
            },
          },
        ],
      });
    }

    showRoundResult(state, onNext) {
      if (!state.roundResult) return;
      const r = state.roundResult;
      const rows = r.rows
        .slice()
        .sort((a, b) => b.delta - a.delta)
        .map((row) => {
          const isYou = row.player === 0;
          const result = row.isBidder
            ? `<span class="result-badge ${row.success ? 'ok' : 'fail'}">${row.success ? 'выполнен' : 'не выполнен'}</span>${row.boltAdded ? '<div class="result-marriages"><span class="result-badge fail">болт</span></div>' : ''}${row.boltPenalty ? '<div class="result-marriages"><span class="result-badge fail">3 болта −120</span></div>' : ''}`
            : row.boltPenalty
              ? '<span class="result-badge fail">3 болта −120</span>'
              : row.boltAdded
                ? '<span class="result-badge fail">болт</span>'
                : '—';
          const marriages = row.marriageList.length
            ? `<div class="result-marriages">${row.marriageList
                .map((m) => `<span class="mini-marriage">${SUITS[m.suit].symbol} +${m.value}</span>`)
                .join('')}</div>`
            : '';
          return `
            <tr class="${isYou ? 'is-you' : ''} ${row.isBidder ? 'is-bidder' : ''}">
              <td>
                <div class="name-cell">
                  <span class="table-avatar">${row.player === 0 ? 'В' : row.player === 1 ? 'В' : 'Л'}</span>
                  ${row.name}
                </div>
                ${marriages}
              </td>
              <td>${row.tricks}</td>
              <td class="col-points">${row.trickPoints}</td>
              <td class="col-marriage">${row.marriagePoints ? `+${row.marriagePoints}` : '—'}</td>
              <td>${result}</td>
              <td class="${row.delta >= 0 ? 'delta-pos' : 'delta-neg'}">${delta(row.delta)}</td>
              <td class="score-cell">${fmt(row.score)}</td>
            </tr>
          `;
        })
        .join('');

      const title = r.tie
        ? 'Равенство на тысяче'
        : r.success
          ? `Заказ ${r.contract} выполнен`
          : `Заказ ${r.contract} не выполнен`;
      const sub = r.tie
        ? 'Играем ещё один кон, чтобы определить победителя.'
        : r.bidder === 0
          ? `Вы заказывали ${r.contract}.`
          : `${r.rows[r.bidder].name} заказывал ${r.contract}.`;

      this.openModal({
        kind: 'result',
        kicker: `КОН ${r.round}`,
        title,
        body: `
          <div class="result-summary">
            <div>
              <div class="result-summary-label">${sub}</div>
              <div class="result-summary-value">${r.success ? 'Контракт закрыт' : 'Контракт провален'}</div>
            </div>
            <span class="result-badge ${r.success ? 'ok' : 'fail'}">${r.success ? 'выполнен' : 'не выполнен'}</span>
          </div>
          <table class="result-table">
            <thead>
              <tr>
                <th>Игрок</th><th>Взятки</th><th class="col-points">Очки</th><th class="col-marriage">Марьяж</th><th>Результат</th><th>Итог</th><th>Счёт</th>
              </tr>
            </thead>
            <tbody>${rows}</tbody>
          </table>
          <p style="margin-top:14px;color:var(--muted-2);font-size:11px">
            Заказчик получает ровно свой заказ. Остальные — округлённые очки взяток и марьяжи.
            Игрок без взяток получает болт; третий болт — минус 120.
          </p>
        `,
        wide: true,
        closable: false,
        actions: [
          {
            label: r.gameWinner != null || r.tie ? (r.tie ? 'Продолжить' : 'Итоги игры') : 'Следующий кон',
            style: 'primary',
            onClick: () => {
              if (onNext) onNext();
            },
          },
        ],
      });
    }

    showGameOver(state, callbacks) {
      const cb = callbacks || {};
      const sorted = state.players.slice().sort((a, b) => b.score - a.score);
      const winner = state.gameWinner != null ? state.players[state.gameWinner] : sorted[0];
      const humanWon = state.gameWinner === 0;
      const rows = sorted
        .map((p, index) => `
          <tr class="${p.id === 0 ? 'is-you' : ''} ${p.id === winner.id ? 'is-bidder' : ''}">
            <td>${index + 1}</td>
            <td><div class="name-cell"><span class="table-avatar">${p.id === 0 ? 'В' : p.id === 1 ? 'В' : 'Л'}</span>${p.name}</div></td>
            <td class="score-cell">${fmt(p.score)}</td>
            <td>${p.bolts}</td>
          </tr>
        `)
        .join('');
      this.openModal({
        kind: 'over',
        kicker: 'ПАРТИЯ ЗАВЕРШЕНА',
        title: humanWon ? 'Вы дошли до тысячи' : `Победил ${winner.name}`,
        body: `
          <div class="final-hero">
            <div class="final-crown">${humanWon ? '♛' : '✦'}</div>
            <h3 class="final-title">${humanWon ? 'Победа за вами' : `${winner.name} забирает партию`}</h3>
            <p class="final-sub">${humanWon ? 'Тысяча взята. Ворон и Лиса признают поражение.' : `У вас ${fmt(state.players[0].score)} очков. Ещё один кон — и всё может измениться.`}</p>
          </div>
          <table class="result-table">
            <thead><tr><th>Место</th><th>Игрок</th><th>Очки</th><th>Болты</th></tr></thead>
            <tbody>${rows}</tbody>
          </table>
        `,
        wide: true,
        closable: false,
        actions: [
          {
            label: 'Ещё партия',
            style: 'primary',
            onClick: () => {
              if (cb.onRestart) cb.onRestart();
            },
          },
          {
            label: 'В меню',
            style: 'ghost',
            onClick: () => {
              if (cb.onMenu) cb.onMenu();
            },
          },
        ],
      });
      if (humanWon) this.startConfetti();
    }

    confirmMenu() {
      this.openModal({
        kind: 'confirm',
        kicker: 'ПАРТИЯ',
        title: 'Выйти в меню?',
        body: '<p>Текущая партия не сохранится. Вы сможете начать новую в любой момент.</p>',
        closable: true,
        actions: [
          { label: 'Остаться', style: 'ghost' },
          {
            label: 'Выйти',
            style: 'primary',
            onClick: () => {
              if (this.cb.onMenu) this.cb.onMenu();
            },
          },
        ],
      });
    }

    toast(text, ms) {
      const el = document.createElement('div');
      el.className = 'toast';
      el.textContent = text;
      this.el.toastRoot.appendChild(el);
      while (this.el.toastRoot.children.length > 3) this.el.toastRoot.firstChild.remove();
      const timeout = Number(ms) || 2200;
      setTimeout(() => {
        el.classList.add('fade');
        setTimeout(() => el.remove(), 320);
      }, timeout);
    }

    onKeyDown(event) {
      if (event.key === 'Escape') {
        if (this.modalOpen && this.modalClosable) {
          this.closeModal();
          return;
        }
        if (this.modalOpen) return;
        if (this.state) this.confirmMenu();
        return;
      }
      if (!this.state || this.state.phase !== 'bidding' || this.state.auction.turn !== 0) return;
      if (this.modalOpen) return;
      if (event.key === 'ArrowLeft') {
        event.preventDefault();
        this.adjustBid(-5);
      } else if (event.key === 'ArrowRight') {
        event.preventDefault();
        this.adjustBid(5);
      } else if (event.key === 'Enter') {
        event.preventDefault();
        this.submitBid();
      }
    }

    /* ---------- анимация взятки и конфетти ---------- */

    animateCollect(winner) {
      return new Promise((resolve) => {
        const area = this.el.trickArea;
        area.classList.add('collecting');
        const direction = {
          0: { tx: '0px', ty: '180px' },
          1: { tx: '-360px', ty: '-20px' },
          2: { tx: '360px', ty: '-20px' },
        }[winner] || { tx: '0px', ty: '-60px' };
        area.querySelectorAll('.trick-slot .card').forEach((card) => {
          card.style.setProperty('--tx', direction.tx);
          card.style.setProperty('--ty', direction.ty);
        });
        setTimeout(resolve, 560);
      });
    }

    startConfetti() {
      const canvas = this.el.confetti;
      const ctx = canvas.getContext('2d');
      if (!ctx) return;
      this.stopConfetti();
      const dpr = Math.min(2, root.devicePixelRatio || 1);
      const width = root.innerWidth;
      const height = root.innerHeight;
      canvas.width = width * dpr;
      canvas.height = height * dpr;
      canvas.style.width = `${width}px`;
      canvas.style.height = `${height}px`;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      const colors = ['#e8ba6e', '#f6d9a4', '#fff3d6', '#7fbf9d', '#c95d55', '#d8a352'];
      const pieces = Array.from({ length: 150 }, () => ({
        x: width * (0.25 + Math.random() * 0.5),
        y: height * (0.15 + Math.random() * 0.2),
        vx: (Math.random() - 0.5) * 9,
        vy: -6 - Math.random() * 9,
        gravity: 0.2 + Math.random() * 0.15,
        size: 4 + Math.random() * 7,
        color: colors[Math.floor(Math.random() * colors.length)],
        rotation: Math.random() * Math.PI,
        spin: (Math.random() - 0.5) * 0.25,
        life: 1,
      }));
      const started = performance.now();
      const frame = (now) => {
        const elapsed = now - started;
        ctx.clearRect(0, 0, width, height);
        for (const p of pieces) {
          p.vy += p.gravity;
          p.x += p.vx;
          p.y += p.vy;
          p.vx *= 0.995;
          p.rotation += p.spin;
          p.life = Math.max(0, 1 - elapsed / 3800);
          ctx.save();
          ctx.globalAlpha = p.life;
          ctx.translate(p.x, p.y);
          ctx.rotate(p.rotation);
          ctx.fillStyle = p.color;
          ctx.fillRect(-p.size / 2, -p.size / 4, p.size, p.size / 2);
          ctx.restore();
        }
        if (elapsed < 4000) {
          this.confettiRaf = requestAnimationFrame(frame);
        } else {
          ctx.clearRect(0, 0, width, height);
        }
      };
      this.confettiRaf = requestAnimationFrame(frame);
    }

    stopConfetti() {
      if (this.confettiRaf) cancelAnimationFrame(this.confettiRaf);
      this.confettiRaf = 0;
      const canvas = this.el.confetti;
      const ctx = canvas.getContext('2d');
      if (ctx) ctx.clearRect(0, 0, canvas.width, canvas.height);
    }
  }

  root.GameUI = GameUI;
})(window);
