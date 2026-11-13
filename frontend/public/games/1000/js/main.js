/*!
 * «1000» — контроллер партии: связывает движок, ИИ, интерфейс и звук.
 */
(function (root) {
  'use strict';

  const E = root.ThousandEngine;
  const AI = root.ThousandAI;
  const S = root.ThousandSound;

  const STATS_KEY = 'thousand.stats';
  const SAVE_KEY = 'thousand.save';
  const SAVE_VERSION = 1;

  function loadStats() {
    try {
      const raw = JSON.parse(root.localStorage.getItem(STATS_KEY) || '{}');
      return {
        games: Math.max(0, Number(raw.games) || 0),
        wins: Math.max(0, Number(raw.wins) || 0),
      };
    } catch (_) {
      return { games: 0, wins: 0 };
    }
  }

  function saveStats(stats) {
    try {
      root.localStorage.setItem(STATS_KEY, JSON.stringify(stats));
    } catch (_) {
      /* ignore */
    }
  }

  function randomSeed() {
    const params = new URLSearchParams(root.location.search);
    const fromUrl = Number(params.get('seed'));
    if (Number.isFinite(fromUrl) && fromUrl > 0) return fromUrl;
    return (Date.now() ^ Math.floor(Math.random() * 0x7fffffff)) >>> 0;
  }

  class GameController {
    constructor() {
      this.version = 0;
      this.timer = null;
      this.state = null;
      this.resultShown = false;
      this.gameOverShown = false;
      this.talonShownFor = null;
      this.stats = loadStats();

      this.mode = 'offline';
      this.net = null;
      this.onlineRoom = null;
      this.onlinePending = false;
      this.onlinePendingTimer = null;
      this.onlineResultShown = false;
      this.onlineGameOverShown = false;
      this.onlineLastPhase = null;
      this.onlineContinueSent = false;
      this.onlineTalonShownFor = null;
      this.onlineModalTimer = null;
      this.netClosing = false;

      this.ui = new root.GameUI({
        onStart: () => this.startGame(),
        onResume: () => this.resumeGame(),
        onBid: (amount) => this.humanBid(amount),
        onPass: () => this.humanPass(),
        onTakeTalon: () => this.humanTakeTalon(),
        onDiscard: (cards, recipients) => this.humanDiscard(cards, recipients),
        onCard: (card) => this.humanCard(card),
        onMarriage: (suit) => this.humanMarriage(suit),
        onCancelMarriage: () => this.humanCancelMarriage(),
        onMenu: () => this.openMenu(),
        onModalClose: () => this.resumeAfterModal(),
        onOnlinePanel: () => this.openOnlinePanel(),
        onOnlineCreate: (name) => this.onlineCreate(name),
        onOnlineJoin: (code, name) => this.onlineJoin(code, name),
        onOnlineReady: (ready) => this.onlineReady(ready),
        onOnlineStart: () => this.onlineStart(),
        onOnlineLeave: () => this.leaveOnline(),
      });
      this.ui.updateMenuStats(this.stats);
      this.ui.setSaveAvailable(this.hasSave());
      this.ui.showMenu();
      root.__THOUSAND__ = this;
    }

    /* ---------- онлайн: подключение и комнаты ---------- */

    openOnlinePanel() {
      this.ui.showOnlinePanel();
    }

    ensureNet() {
      if (this.net && this.net.isOpen()) return Promise.resolve();
      if (this.net) this.net.close();
      this.netClosing = false;
      this.net = new root.ThousandNet({
        onMessage: (message) => this.handleNetMessage(message),
        onClose: () => this.handleNetClose(),
      });
      return this.net
        .connect()
        .then(() => {
          this.mode = 'online';
        })
        .catch((error) => {
          this.net = null;
          this.mode = 'offline';
          throw error;
        });
    }

    onlineCreate(name) {
      this.ensureNet()
        .then(() => {
          this.mode = 'online';
          this.net.send({ t: 'create', name });
        })
        .catch((error) => {
          this.ui.onlineError(error.message || 'Не удалось подключиться к серверу');
        });
    }

    onlineJoin(code, name) {
      this.ensureNet()
        .then(() => {
          this.mode = 'online';
          this.net.send({ t: 'join', code, name });
        })
        .catch((error) => {
          this.ui.onlineError(error.message || 'Не удалось подключиться к серверу');
        });
    }

    onlineReady(ready) {
      if (!this.net || !this.net.isOpen()) return;
      this.net.send({ t: 'ready', ready: Boolean(ready) });
    }

    onlineStart() {
      if (!this.net || !this.net.isOpen()) return;
      this.net.send({ t: 'start' });
    }

    leaveOnline() {
      this.netClosing = true;
      if (this.net) {
        this.net.send({ t: 'leave' });
        this.net.close();
      }
      this.net = null;
      this.mode = 'offline';
      this.version += 1;
      this.clearTimer();
      this.state = null;
      this.onlineRoom = null;
      this.clearOnlinePending();
      this.onlineResultShown = false;
      this.onlineGameOverShown = false;
      this.onlineLastPhase = null;
      this.onlineContinueSent = false;
      this.onlineTalonShownFor = null;
      this.ui.closeModal();
      this.ui.showMenu();
      this.ui.setOnlineRoom(null);
      S.play('click');
      this.debug();
    }

    closeNet() {
      this.netClosing = true;
      if (this.net) this.net.close();
      this.net = null;
      this.netClosing = false;
    }

    clearOnlinePending() {
      this.onlinePending = false;
      if (this.onlinePendingTimer) {
        clearTimeout(this.onlinePendingTimer);
        this.onlinePendingTimer = null;
      }
    }

    handleNetMessage(message) {
      if (!message || typeof message.t !== 'string') return;
      switch (message.t) {
        case 'room':
          this.handleRoom(message.room);
          return;
        case 'state':
          this.handleOnlineState(message.view);
          return;
        case 'error':
          this.clearOnlinePending();
          this.ui.onlineError(message.message || 'Сервер отклонил действие');
          if (this.onlineRoom && this.ui.modalKind === 'lobby') this.ui.showLobby(this.onlineRoom);
          return;
        case 'toast':
          this.ui.toast(message.text, 2600);
          return;
        case 'notice':
          this.ui.toast(message.text, 4200);
          return;
        case 'closed':
          this.mode = 'offline';
          this.net = null;
          this.state = null;
          this.onlineRoom = null;
          this.clearOnlinePending();
          this.onlineResultShown = false;
          this.onlineGameOverShown = false;
          this.onlineLastPhase = null;
          this.onlineContinueSent = false;
          this.onlineTalonShownFor = null;
          this.ui.closeModal();
          this.ui.showMenu();
          this.ui.setOnlineRoom(null);
          this.ui.toast(message.text || 'Комната закрыта', 4200);
          return;
        default:
          return;
      }
    }

    handleRoom(room) {
      if (!room) return;
      const alreadyLobby = this.ui.modalKind === 'lobby';
      this.mode = 'online';
      this.onlineRoom = room;
      this.state = null;
      this.clearOnlinePending();
      this.onlineResultShown = false;
      this.onlineGameOverShown = false;
      this.onlineLastPhase = null;
      this.onlineContinueSent = false;
      this.onlineTalonShownFor = null;
      if (this.onlineModalTimer) {
        clearTimeout(this.onlineModalTimer);
        this.onlineModalTimer = null;
      }
      if (!alreadyLobby) {
        this.ui.closeModal();
        this.ui.showMenu();
      }
      this.ui.setOnlineRoom(room.code);
      this.ui.showLobby(room);
      this.debug();
    }

    handleOnlineState(view) {
      if (!view) return;
      this.mode = 'online';
      this.state = view;
      this.clearOnlinePending();
      this.onlineRoom = { code: view.roomCode, phase: 'playing' };
      const nextPhase = view.phase;
      const prevPhase = this.onlineLastPhase;
      this.onlineLastPhase = nextPhase;

      if (prevPhase !== nextPhase) {
        if (nextPhase !== 'scoring' && this.ui.modalKind === 'result') this.ui.closeModal();
        if (nextPhase !== 'gameover' && this.ui.modalKind === 'over') this.ui.closeModal();
        if (nextPhase === 'scoring') {
          this.onlineResultShown = false;
          this.onlineContinueSent = false;
        }
        if (nextPhase === 'gameover') {
          this.onlineGameOverShown = false;
        }
      }

      this.ui.closeModalIfKind('online');
      this.ui.closeModalIfKind('lobby');
      this.ui.showGame();
      this.ui.setOnlineRoom(view.roomCode);
      this.ui.render(view);

      if (nextPhase !== 'talon' && this.ui.modalKind === 'talon') this.ui.closeModal();
      if (nextPhase === 'talon' && view.bidder === 0 && this.ui.modalKind !== 'talon') {
        if (this.onlineTalonShownFor !== view.round) {
          this.onlineTalonShownFor = view.round;
          this.ui.showTalon(view);
        }
      }
      if (nextPhase !== 'talon') this.onlineTalonShownFor = null;

      if (nextPhase === 'scoring' && !this.onlineResultShown) {
        this.onlineResultShown = true;
        if (this.onlineModalTimer) clearTimeout(this.onlineModalTimer);
        this.onlineModalTimer = setTimeout(() => {
          if (!this.state || this.state.phase !== 'scoring') return;
          this.ui.showRoundResult(this.state, () => this.onlineContinue());
        }, 360);
      } else if (nextPhase === 'gameover' && !this.onlineGameOverShown) {
        this.onlineGameOverShown = true;
        this.ui.showGameOver(view, {
          onRestart: () => this.onlineRematch(),
          onMenu: () => this.leaveOnline(),
        });
      }
      this.debug();
    }

    onlineContinue() {
      if (this.mode !== 'online' || this.onlineContinueSent) return;
      if (!this.net || !this.net.isOpen()) {
        this.ui.toast('Связь с сервером потеряна');
        return;
      }
      this.onlineContinueSent = true;
      this.net.send({ t: 'continue' });
    }

    onlineRematch() {
      if (!this.net || !this.net.isOpen()) {
        this.ui.toast('Связь с сервером потеряна');
        return;
      }
      this.net.send({ t: 'rematch' });
    }

    handleNetClose() {
      if (this.netClosing) return;
      if (this.mode !== 'online') return;
      this.mode = 'offline';
      this.net = null;
      this.state = null;
      this.onlineRoom = null;
      this.clearOnlinePending();
      this.onlineResultShown = false;
      this.onlineGameOverShown = false;
      this.onlineLastPhase = null;
      this.onlineContinueSent = false;
      this.onlineTalonShownFor = null;
      this.ui.closeModal();
      this.ui.showMenu();
      this.ui.setOnlineRoom(null);
      this.ui.toast('Связь с сервером потеряна. Начните новую игру.', 5000);
    }

    sendOnline(message) {
      if (this.mode !== 'online' || !this.net || !this.net.isOpen()) {
        this.ui.toast('Нет связи с сервером');
        return false;
      }
      if (this.onlinePending) {
        this.ui.toast('Ждём ответ сервера…');
        return false;
      }
      if (!this.net.send(message)) {
        this.ui.toast('Нет связи с сервером');
        return false;
      }
      this.onlinePending = true;
      if (this.onlinePendingTimer) clearTimeout(this.onlinePendingTimer);
      this.onlinePendingTimer = setTimeout(() => {
        this.onlinePending = false;
        this.onlinePendingTimer = null;
      }, 3000);
      return true;
    }

    /* ---------- запуск и завершение ---------- */

    startGame() {
      this.closeNet();
      this.mode = 'offline';
      this.clearOnlinePending();
      this.onlineLastPhase = null;
      this.onlineTalonShownFor = null;
      this.onlineResultShown = false;
      this.onlineGameOverShown = false;
      this.version += 1;
      this.clearTimer();
      this.state = null;
      this.ui.closeModal();
      this.ui.setOnlineRoom(null);
      this.resultShown = false;
      this.gameOverShown = false;
      this.talonShownFor = null;
      this.ui.stopConfetti();
      this.state = E.createGame({
        seed: randomSeed(),
        names: ['Вы', 'Ворон', 'Лиса'],
      });
      this.ui.resetView();
      this.ui.showGame();
      S.init();
      S.play('deal');
      this.ui.render(this.state);
      this.tick(this.version);
      this.saveGame();
      this.debug();
    }

    /* ---------- сохранение офлайн-партии ---------- */

    hasSave() {
      try {
        const data = JSON.parse(root.localStorage.getItem(SAVE_KEY) || 'null');
        return Boolean(data && data.v === SAVE_VERSION && data.state && data.state.phase !== 'gameover');
      } catch (_) {
        return false;
      }
    }

    saveGame() {
      if (this.mode !== 'offline' || !this.state || this.state.phase === 'gameover') return;
      try {
        const rng = this.state.rng && this.state.rng.getState ? this.state.rng.getState() : null;
        root.localStorage.setItem(
          SAVE_KEY,
          JSON.stringify({ v: SAVE_VERSION, savedAt: Date.now(), rng, state: this.state })
        );
        this.ui.setSaveAvailable(true);
      } catch (_) {
        /* ignore */
      }
    }

    clearSave() {
      try {
        root.localStorage.removeItem(SAVE_KEY);
      } catch (_) {
        /* ignore */
      }
      this.ui.setSaveAvailable(false);
    }

    resumeGame() {
      let data = null;
      try {
        data = JSON.parse(root.localStorage.getItem(SAVE_KEY) || 'null');
      } catch (_) {
        data = null;
      }
      if (!data || data.v !== SAVE_VERSION || !data.state) {
        this.clearSave();
        return;
      }
      this.closeNet();
      this.mode = 'offline';
      this.clearOnlinePending();
      this.onlineLastPhase = null;
      this.version += 1;
      this.clearTimer();
      const state = data.state;
      state.rng = E.createRng(state.seed);
      if (data.rng != null && state.rng.setState) state.rng.setState(data.rng);
      this.state = state;
      this.ui.setOnlineRoom(null);
      this.resultShown = false;
      this.gameOverShown = false;
      this.talonShownFor = null;
      this.ui.stopConfetti();
      this.ui.resetView();
      this.ui.showGame();
      S.init();
      this.ui.render(this.state);
      this.tick(this.version);
      this.ui.setSaveAvailable(true);
      this.debug();
    }

    openMenu() {
      if (this.mode === 'online') {
        this.leaveOnline();
        return;
      }
      this.version += 1;
      this.clearTimer();
      this.state = null;
      this.resultShown = false;
      this.gameOverShown = false;
      this.talonShownFor = null;
      this.ui.closeModal();
      this.ui.showMenu();
      S.play('click');
      this.debug();
    }

    clearTimer() {
      if (this.timer) {
        clearTimeout(this.timer);
        this.timer = null;
      }
    }

    isPaused() {
      return Boolean(this.ui.modalOpen && ['rules', 'scores', 'confirm'].includes(this.ui.modalKind));
    }

    resumeAfterModal() {
      if (this.state) this.tick(this.version);
    }

    schedule(fn, delay) {
      this.clearTimer();
      const version = this.version;
      this.timer = setTimeout(() => {
        this.timer = null;
        if (version !== this.version) return;
        fn(version);
      }, delay);
    }

    debug() {
      root.__THOUSAND__ = this;
    }

    /* ---------- главный цикл ---------- */

    tick(version) {
      if (version !== this.version) return;
      if (this.mode === 'online') return;
      const s = this.state;
      if (!s) return;
      this.clearTimer();
      if (this.ui.modalOpen && ['rules', 'scores', 'confirm'].includes(this.ui.modalKind)) return;

      switch (s.phase) {
        case 'bidding':
          if (s.auction && s.auction.turn !== 0) {
            this.schedule(() => this.botBid(version), 620 + Math.random() * 420);
          }
          return;

        case 'talon':
          if (s.bidder === 0) {
            if (this.talonShownFor !== s.round && this.ui.modalKind !== 'talon') {
              this.talonShownFor = s.round;
              this.ui.showTalon(s);
            }
          } else {
            this.schedule(() => this.botTalon(version), 1200);
          }
          return;

        case 'discard':
          if (s.bidder !== 0) {
            this.schedule(() => this.botDiscard(version), 780);
          }
          return;

        case 'playing':
          if (s.trickComplete) {
            this.schedule(() => this.collectTrick(version), 560);
          } else if (s.turn !== 0) {
            this.schedule(() => this.botPlay(version), 560 + Math.random() * 360);
          }
          return;

        case 'scoring':
          if (!this.resultShown) {
            this.resultShown = true;
            this.schedule(() => this.presentResult(version), 420);
          }
          return;

        case 'gameover':
          this.showGameOver();
          return;

        default:
          return;
      }
    }

    after(previousPhase, sound) {
      const s = this.state;
      if (!s) return;
      if (sound) S.play(sound);
      this.ui.render(s);

      if (s.phase !== previousPhase) {
        if (s.phase === 'scoring') this.resultShown = false;
        if (s.phase === 'talon' && s.bidder === 0) {
          this.talonShownFor = s.round;
          this.ui.showTalon(s);
        }
        if ((s.phase === 'discard' || s.phase === 'playing') && this.ui.modalKind === 'talon') {
          this.ui.closeModal();
        }
      }
      this.tick(this.version);
      this.saveGame();
      this.debug();
    }

    /* ---------- боты ---------- */

    botBid(version) {
      if (version !== this.version) return;
      if (this.isPaused()) return;
      const s = this.state;
      if (!s || s.phase !== 'bidding' || !s.auction) return;
      const p = s.auction.turn;
      if (p === 0) return;
      const prev = s.phase;
      let action = 'pass';
      try {
        action = AI.chooseBid(s, p, Math.random);
        if (action === 'pass') E.pass(s, p);
        else E.bid(s, p, action);
      } catch (error) {
        console.warn('botBid', error);
        try {
          if (s.auction.high === 0) E.bid(s, p, 100);
          else E.pass(s, p);
        } catch (_) {
          /* состояние уже изменилось — просто продолжаем */
        }
      }
      this.after(prev, action === 'pass' ? 'pass' : 'bid');
    }

    botTalon(version) {
      if (version !== this.version) return;
      if (this.isPaused()) return;
      const s = this.state;
      if (!s || s.phase !== 'talon' || s.bidder === 0) return;
      const prev = s.phase;
      try {
        E.takeTalon(s);
        this.after(prev, 'deal');
      } catch (error) {
        console.warn('botTalon', error);
        this.tick(version);
      }
    }

    botDiscard(version) {
      if (version !== this.version) return;
      if (this.isPaused()) return;
      const s = this.state;
      if (!s || s.phase !== 'discard' || s.bidder === 0) return;
      const prev = s.phase;
      try {
        const choice = AI.chooseDiscard(s, s.bidder);
        E.discard(s, s.bidder, choice.cardIds, choice.recipients);
        const toHuman = choice.recipients.indexOf(0);
        if (toHuman >= 0) {
          this.ui.toast(`Вам снос: ${E.cardLongName(choice.cardIds[toHuman])}`, 2600);
        }
        this.after(prev, 'deal');
      } catch (error) {
        console.warn('botDiscard', error);
        this.tick(version);
      }
    }

    botPlay(version) {
      if (version !== this.version) return;
      if (this.isPaused()) return;
      const s = this.state;
      if (!s || s.phase !== 'playing' || s.turn === 0 || s.trickComplete) return;
      const p = s.turn;

      if (!s.trick.length) {
        const suit = AI.chooseMarriage(s, p);
        if (suit) {
          try {
            E.declareMarriage(s, p, suit);
            this.ui.toast(`${s.players[p].name}: марьяж ${E.SUITS[suit].symbol} +${E.SUITS[suit].marriage}`, 2200);
            this.ui.render(s);
            S.play('marriage');
            this.tick(this.version);
            this.debug();
            return;
          } catch (error) {
            console.warn('botMarriage', error);
          }
        }
      }

      const prev = s.phase;
      try {
        const card = AI.chooseCard(s, p, Math.random);
        if (!card || !E.isLegal(s, p, card)) throw new Error('ИИ не нашёл карту');
        E.playCard(s, p, card);
        this.after(prev, 'card');
      } catch (error) {
        console.warn('botPlay', error);
        const fallback = E.legalCards(s, p)[0];
        if (fallback) {
          try {
            E.playCard(s, p, fallback);
            this.after(prev, 'card');
            return;
          } catch (_) {
            /* ignore */
          }
        }
        this.tick(version);
      }
    }

    collectTrick(version) {
      if (version !== this.version) return;
      if (this.isPaused()) return;
      const s = this.state;
      if (!s || s.phase !== 'playing' || !s.trickComplete || !s.lastTrick) return;
      const winner = s.lastTrick.winner;
      const prev = s.phase;
      this.ui.animateCollect(winner).then(() => {
        if (version !== this.version) return;
        try {
          E.collectTrick(s);
          this.after(prev, 'trick');
        } catch (error) {
          console.warn('collectTrick', error);
          this.tick(version);
        }
      });
    }

    presentResult(version) {
      if (version !== this.version) return;
      if (this.isPaused()) {
        this.resultShown = false;
        return;
      }
      const s = this.state;
      if (!s || s.phase !== 'scoring' || !s.roundResult) return;
      const row = s.roundResult.rows[0];
      S.play(row.isBidder ? (row.success ? 'score' : 'warn') : 'score');
      this.ui.showRoundResult(s, () => this.nextRound());
    }

    nextRound() {
      const s = this.state;
      if (!s) return;
      this.ui.closeModal();

      if (s.gameWinner != null) {
        this.stats.games += 1;
        if (s.gameWinner === 0) this.stats.wins += 1;
        saveStats(this.stats);
        this.ui.updateMenuStats(this.stats);
        try {
          E.nextRound(s);
        } catch (_) {
          s.phase = 'gameover';
        }
        this.showGameOver();
        return;
      }

      const wasTie = Boolean(s.gameTie);
      try {
        E.nextRound(s);
      } catch (error) {
        console.warn('nextRound', error);
        return;
      }
      this.resultShown = false;
      this.gameOverShown = false;
      this.talonShownFor = null;
      S.play('deal');
      this.ui.render(s);
      if (wasTie) this.ui.toast('Равенство на тысяче — играем ещё кон');
      this.tick(this.version);
      this.debug();
    }

    showGameOver() {
      if (this.gameOverShown) return;
      const s = this.state;
      if (!s) return;
      this.gameOverShown = true;
      this.resultShown = true;
      this.clearSave();
      this.ui.closeModal();
      this.ui.showGameOver(s, {
        onRestart: () => this.startGame(),
        onMenu: () => this.openMenu(),
      });
      S.play(s.gameWinner === 0 ? 'win' : 'lose');
    }

    /* ---------- действия человека ---------- */

    humanBid(amount) {
      if (this.mode === 'online') {
        this.sendOnline({ t: 'bid', amount });
        return;
      }
      const s = this.state;
      if (!s || s.phase !== 'bidding') return;
      const prev = s.phase;
      try {
        E.bid(s, 0, amount);
        this.after(prev, 'bid');
      } catch (error) {
        this.ui.toast(error.message);
        S.play('warn');
      }
    }

    humanPass() {
      if (this.mode === 'online') {
        this.sendOnline({ t: 'pass' });
        return;
      }
      const s = this.state;
      if (!s || s.phase !== 'bidding') return;
      const prev = s.phase;
      try {
        E.pass(s, 0);
        this.after(prev, 'pass');
      } catch (error) {
        this.ui.toast(error.message);
        S.play('warn');
      }
    }

    humanTakeTalon() {
      if (this.mode === 'online') {
        this.sendOnline({ t: 'takeTalon' });
        return;
      }
      const s = this.state;
      if (!s || s.phase !== 'talon') return;
      const prev = s.phase;
      try {
        E.takeTalon(s);
        this.after(prev, 'deal');
      } catch (error) {
        this.ui.toast(error.message);
      }
    }

    humanDiscard(cards, recipients) {
      if (this.mode === 'online') {
        this.sendOnline({ t: 'discard', cards, to: recipients });
        return;
      }
      const s = this.state;
      if (!s || s.phase !== 'discard') return;
      const prev = s.phase;
      try {
        E.discard(s, 0, cards, recipients);
        this.after(prev, 'deal');
      } catch (error) {
        this.ui.toast(error.message);
        S.play('warn');
      }
    }

    humanCard(card) {
      if (this.mode === 'online') {
        this.sendOnline({ t: 'play', card });
        return;
      }
      const s = this.state;
      if (!s || s.phase !== 'playing' || s.turn !== 0 || s.trickComplete) return;
      // Заход королём или дамой при необъявленном марьяже: предлагаем объявить.
      const suit = card[0];
      const rank = card.slice(1);
      const canAnnounce =
        !s.pendingMarriage &&
        !s.trick.length &&
        s.trickNumber >= 1 &&
        (rank === 'K' || rank === 'Q') &&
        E.marriageSuits(s.hands[0]).includes(suit) &&
        !s.declared[0].includes(suit);
      if (canAnnounce) {
        this.ui.askMarriage(suit, (announce) => this.playHumanCard(card, announce ? suit : null));
        return;
      }
      this.playHumanCard(card, null);
    }

    playHumanCard(card, marriageSuit) {
      const s = this.state;
      if (!s || s.phase !== 'playing' || s.turn !== 0) return;
      const prev = s.phase;
      try {
        if (marriageSuit) {
          E.declareMarriage(s, 0, marriageSuit);
          S.play('marriage');
        }
        E.playCard(s, 0, card);
        this.after(prev, 'card');
      } catch (error) {
        this.ui.toast(error.message);
        S.play('warn');
      }
    }

    humanMarriage(suit) {
      if (this.mode === 'online') {
        this.sendOnline({ t: 'marriage', suit });
        return;
      }
      const s = this.state;
      if (!s || s.phase !== 'playing' || s.turn !== 0) return;
      try {
        E.declareMarriage(s, 0, suit);
        this.ui.toast(`Марьяж ${E.SUITS[suit].symbol} +${E.SUITS[suit].marriage} — зайдите королём или дамой`, 2600);
        S.play('marriage');
        this.ui.render(s);
        this.tick(this.version);
        this.debug();
      } catch (error) {
        this.ui.toast(error.message);
        S.play('warn');
      }
    }

    humanCancelMarriage() {
      if (this.mode === 'online') {
        this.sendOnline({ t: 'cancelMarriage' });
        return;
      }
      const s = this.state;
      if (!s || s.phase !== 'playing') return;
      E.cancelMarriage(s, 0);
      this.ui.render(s);
      this.tick(this.version);
    }
  }

  function boot() {
    const controller = new GameController();
    root.__THOUSAND__ = controller;
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})(window);
