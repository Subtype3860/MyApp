/*!
 * «1000» — движок карточной игры «Тысяча».
 * Чистый JS без зависимостей: работает и в браузере, и в Node для тестов.
 */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.ThousandEngine = factory();
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  const PLAYER_COUNT = 3;
  const HAND_SIZE = 7;
  const TALON_SIZE = 3;
  const CARDS_PER_ROUND = 24;

  const RANKS = ['9', 'J', 'Q', 'K', '10', 'A'];
  const RANK_POWER = { 9: 0, J: 1, Q: 2, K: 3, 10: 4, A: 5 };
  const RANK_LABEL = { 9: '9', J: 'В', Q: 'Д', K: 'К', 10: '10', A: 'Т' };
  const CARD_VALUE = { 9: 0, J: 2, Q: 3, K: 4, 10: 10, A: 11 };

  const SUITS = {
    S: { key: 'S', symbol: '♠', name: 'пики', adj: 'пиковый', color: 'black', marriage: 40, order: 0 },
    C: { key: 'C', symbol: '♣', name: 'трефы', adj: 'трефовый', color: 'black', marriage: 60, order: 1 },
    D: { key: 'D', symbol: '♦', name: 'бубны', adj: 'бубновый', color: 'red', marriage: 80, order: 2 },
    H: { key: 'H', symbol: '♥', name: 'черви', adj: 'червовый', color: 'red', marriage: 100, order: 3 },
  };
  const SUIT_KEYS = ['S', 'C', 'D', 'H'];
  const DEFAULT_NAMES = ['Вы', 'Ворон', 'Лиса'];

  const next = (p) => (p + 1) % PLAYER_COUNT;
  const prev = (p) => (p + PLAYER_COUNT - 1) % PLAYER_COUNT;

  function suitOf(card) {
    return card[0];
  }

  function rankOf(card) {
    return card.slice(1);
  }

  function cardValue(card) {
    return CARD_VALUE[rankOf(card)] || 0;
  }

  function cardPower(card) {
    return RANK_POWER[rankOf(card)] || 0;
  }

  function cardName(card, accusative) {
    const s = SUITS[suitOf(card)];
    const r = RANK_LABEL[rankOf(card)];
    return `${r}${s.symbol}`;
  }

  function cardLongName(card) {
    const s = SUITS[suitOf(card)];
    const map = { 9: 'девятка', J: 'валет', Q: 'дама', K: 'король', 10: 'десятка', A: 'туз' };
    return `${map[rankOf(card)]} ${s.name}`;
  }

  function createRng(seed) {
    let a = (Number(seed) || 1) >>> 0;
    return function rng() {
      a |= 0;
      a = (a + 0x6d2b79f5) | 0;
      let t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  function shuffle(array, rng) {
    for (let i = array.length - 1; i > 0; i -= 1) {
      const j = Math.floor(rng() * (i + 1));
      const tmp = array[i];
      array[i] = array[j];
      array[j] = tmp;
    }
    return array;
  }

  function sortHand(hand) {
    hand.sort((a, b) => {
      const sa = SUITS[suitOf(a)].order;
      const sb = SUITS[suitOf(b)].order;
      if (sa !== sb) return sa - sb;
      return cardPower(b) - cardPower(a);
    });
    return hand;
  }

  function round5(points) {
    return Math.round(points / 5) * 5;
  }

  function marriageSuits(hand) {
    const flags = { S: 0, C: 0, D: 0, H: 0 };
    for (const card of hand) {
      const r = rankOf(card);
      if (r === 'K') flags[suitOf(card)] |= 1;
      if (r === 'Q') flags[suitOf(card)] |= 2;
    }
    return SUIT_KEYS.filter((s) => flags[s] === 3);
  }

  function maxBidFor(state, player) {
    const sum = marriageSuits(state.hands[player]).reduce((acc, s) => acc + SUITS[s].marriage, 0);
    return 120 + sum;
  }

  function playerName(state, p) {
    return state.players[p].name;
  }

  function pushLog(state, text, kind, player) {
    state.log.push({ text, kind: kind || 'info', player: player == null ? null : player });
    if (state.log.length > 80) state.log.splice(0, state.log.length - 80);
  }

  function createGame(options) {
    const opts = options || {};
    const seed = opts.seed == null ? Date.now() : opts.seed;
    const names = opts.names || DEFAULT_NAMES;
    const rng = createRng(seed);
    const dealer = opts.dealer == null ? Math.floor(rng() * PLAYER_COUNT) : opts.dealer;
    const state = {
      seed,
      rng,
      players: [0, 1, 2].map((i) => ({
        id: i,
        name: names[i] || DEFAULT_NAMES[i],
        isHuman: i === 0,
        score: 0,
        bolts: 0,
        roundBid: null,
      })),
      dealer,
      round: 0,
      phase: 'idle',
      hands: [[], [], []],
      talon: [],
      talonOwner: null,
      talonPublic: true,
      talonTaken: false,
      auction: null,
      bidder: null,
      contract: 0,
      trump: null,
      marriages: [],
      declared: [[], [], []],
      trick: [],
      trickNumber: 0,
      leader: null,
      turn: null,
      pendingMarriage: null,
      tricksWon: [[], [], []],
      trickCounts: [0, 0, 0],
      trickComplete: false,
      lastTrick: null,
      lastDiscard: null,
      playedCards: [],
      trickHistory: [],
      roundResult: null,
      gameWinner: null,
      gameTie: false,
      log: [],
      stats: { tricksPlayed: 0, roundsPlayed: 0 },
    };
    startRound(state);
    return state;
  }

  function startRound(state) {
    if (state.gameWinner) {
      state.phase = 'gameover';
      return state;
    }
    state.round += 1;
    state.stats.roundsPlayed += 1;
    state.phase = 'dealing';
    state.talon = [];
    state.talonOwner = null;
    state.talonPublic = true;
    state.talonTaken = false;
    state.bidder = null;
    state.contract = 0;
    state.trump = null;
    state.marriages = [];
    state.declared = [[], [], []];
    state.trick = [];
    state.trickNumber = 0;
    state.leader = null;
    state.turn = null;
    state.pendingMarriage = null;
    state.tricksWon = [[], [], []];
    state.trickCounts = [0, 0, 0];
    state.trickComplete = false;
    state.lastTrick = null;
    state.lastDiscard = null;
    state.playedCards = [];
    state.trickHistory = [];
    state.roundResult = null;
    state.gameTie = false;
    state.log = [];
    state.players.forEach((p) => {
      p.roundBid = null;
    });

    const deck = [];
    for (const s of SUIT_KEYS) for (const r of RANKS) deck.push(s + r);
    shuffle(deck, state.rng);

    state.hands = [[], [], []];
    let index = 0;
    for (let row = 0; row < HAND_SIZE; row += 1) {
      for (let step = 1; step <= PLAYER_COUNT; step += 1) {
        const p = (state.dealer + step) % PLAYER_COUNT;
        state.hands[p].push(deck[index]);
        index += 1;
      }
    }
    state.talon = deck.slice(index, index + TALON_SIZE);
    state.hands.forEach(sortHand);

    const first = next(state.dealer);
    state.auction = {
      high: 0,
      highBidder: null,
      active: [true, true, true],
      turn: first,
      firstBidder: first,
      history: [],
      bids: 0,
      passes: 0,
      done: false,
    };
    state.phase = 'bidding';
    state.turn = first;
    pushLog(
      state,
      `Кон ${state.round}. Сдающий: ${playerName(state, state.dealer)}. Первым торгуется: ${playerName(state, first)}.`,
      'round'
    );
    return state;
  }

  function activeCount(state) {
    return state.auction.active.filter(Boolean).length;
  }

  function finishAuction(state) {
    const a = state.auction;
    if (!a || a.done) return state;
    a.done = true;
    state.bidder = a.highBidder;
    state.contract = a.high;
    state.players.forEach((p) => {
      p.roundBid = p.id === state.bidder ? state.contract : null;
    });    state.talonPublic = !(state.contract === 100 && a.bids === 1);
    state.talonOwner = state.bidder;
    state.hands[state.bidder].push(...state.talon);
    sortHand(state.hands[state.bidder]);
    state.phase = 'talon';
    state.turn = null;
    pushLog(
      state,
      `Торги за ${state.contract}: ${playerName(state, state.bidder)}.`,
      'auction',
      state.bidder
    );
    if (!state.talonPublic) {
      pushLog(state, 'Прикуп остаётся закрытым: игра на сотне.', 'info');
    }
    return state;
  }

  function advanceAuction(state) {
    if (activeCount(state) === 1) {
      finishAuction(state);
      return state;
    }
    let p = next(state.auction.turn);
    for (let i = 0; i < PLAYER_COUNT; i += 1) {
      if (state.auction.active[p]) {
        state.auction.turn = p;
        state.turn = p;
        return state;
      }
      p = next(p);
    }
    finishAuction(state);
    return state;
  }

  function bid(state, player, amount) {
    const a = state.auction;
    if (state.phase !== 'bidding' || !a) throw new Error('Сейчас не торги');
    if (a.turn !== player) throw new Error('Не ваш ход в торгах');
    if (!a.active[player]) throw new Error('Вы уже пасовали');
    const value = Number(amount);
    if (!Number.isFinite(value) || value % 5 !== 0) throw new Error('Ставка должна быть кратна 5');
    const min = a.high === 0 ? 100 : a.high + 5;
    if (value < min) throw new Error(`Минимальная ставка — ${min}`);
    const max = maxBidFor(state, player);
    if (value > max) throw new Error(`Максимум для этой руки — ${max}`);
    a.high = value;
    a.highBidder = player;
    a.bids += 1;
    a.history.push({ player, type: 'bid', amount: value });
    state.players.forEach((p) => {
      p.roundBid = null;
    });
    state.players[player].roundBid = value;
    pushLog(state, `${playerName(state, player)}: ${value}`, 'bid', player);
    advanceAuction(state);
    return state;
  }

  function pass(state, player) {
    const a = state.auction;
    if (state.phase !== 'bidding' || !a) throw new Error('Сейчас не торги');
    if (a.turn !== player) throw new Error('Не ваш ход в торгах');
    if (!a.active[player]) throw new Error('Вы уже пасовали');
    if (a.high === 0) throw new Error('Первая рука обязана заявить 100');
    a.active[player] = false;
    a.passes += 1;
    a.history.push({ player, type: 'pass' });
    pushLog(state, `${playerName(state, player)}: пас`, 'pass', player);
    if (activeCount(state) === 1) {
      finishAuction(state);
      return state;
    }
    advanceAuction(state);
    return state;
  }

  function takeTalon(state) {
    if (state.phase !== 'talon') throw new Error('Прикуп сейчас недоступен');
    state.talonTaken = true;
    state.phase = 'discard';
    state.turn = state.bidder;
    pushLog(state, `Прикуп: ${playerName(state, state.bidder)}.`, 'info', state.bidder);
    return state;
  }

  function discard(state, player, cardIds, recipients) {
    if (state.phase !== 'discard') throw new Error('Сейчас не снос');
    if (player !== state.bidder) throw new Error('Снос делает только победитель торгов');
    if (!Array.isArray(cardIds) || cardIds.length !== 2) throw new Error('Нужно снести две карты');
    const unique = new Set(cardIds);
    if (unique.size !== 2) throw new Error('Карты должны быть разными');
    const hand = state.hands[player];
    for (const id of cardIds) {
      if (!hand.includes(id)) throw new Error(`Карты ${id} нет на руке`);
    }
    const opponents = [next(player), next(next(player))];
    const dest = Array.isArray(recipients) ? recipients.slice() : opponents.slice();
    if (dest.length !== 2 || new Set(dest).size !== 2) throw new Error('Нужно по одной карте каждому сопернику');
    for (const p of dest) {
      if (!opponents.includes(p)) throw new Error('Сносить можно только соперникам');
    }
    const cards = cardIds.slice();
    state.hands[player] = hand.filter((id) => !unique.has(id));
    for (let i = 0; i < 2; i += 1) {
      state.hands[dest[i]].push(cards[i]);
      sortHand(state.hands[dest[i]]);
    }
    sortHand(state.hands[player]);
    state.lastDiscard = {
      from: player,
      cards: cards.slice(),
      to: dest.slice(),
      public: false,
    };
    pushLog(state, `Снос: ${playerName(state, player)}.`, 'info', player);

    state.phase = 'playing';
    state.trump = null;
    state.marriages = [];
    state.declared = [[], [], []];
    state.trick = [];
    state.trickNumber = 0;
    state.tricksWon = [[], [], []];
    state.trickCounts = [0, 0, 0];
    state.leader = player;
    state.turn = player;
    state.pendingMarriage = null;
    state.trickComplete = false;
    state.lastTrick = null;
    state.talonTaken = true;
    return state;
  }

  function legalCards(state, player) {
    if (state.phase !== 'playing' || player == null) return [];
    const hand = state.hands[player].slice();
    if (!hand.length) return [];
    if (state.pendingMarriage && state.pendingMarriage.player === player) {
      const suit = state.pendingMarriage.suit;
      return hand.filter((c) => suitOf(c) === suit && (rankOf(c) === 'K' || rankOf(c) === 'Q'));
    }
    if (!state.trick.length) return hand;
    const led = suitOf(state.trick[0].card);
    const following = hand.filter((c) => suitOf(c) === led);
    if (following.length) return following;
    if (state.trump) {
      const trumps = hand.filter((c) => suitOf(c) === state.trump);
      if (trumps.length) return trumps;
    }
    return hand;
  }

  function isLegal(state, player, cardId) {
    return legalCards(state, player).includes(cardId);
  }

  function declareMarriage(state, player, suit) {
    if (state.phase !== 'playing') throw new Error('Сейчас не розыгрыш');
    if (state.turn !== player) throw new Error('Не ваш ход');
    if (state.trick.length) throw new Error('Марьяж объявляют только с захода');
    if (state.trickNumber < 1) throw new Error('На первом ходу марьяж не объявляют');
    if (!SUITS[suit]) throw new Error('Неизвестная масть');
    if (!marriageSuits(state.hands[player]).includes(suit)) throw new Error('У вас нет такого марьяжа');
    if (state.declared[player].includes(suit)) throw new Error('Этот марьяж уже объявлен');
    if (state.pendingMarriage) throw new Error('Сначала завершите объявление');
    state.pendingMarriage = { player, suit, value: SUITS[suit].marriage };
    pushLog(
      state,
      `Марьяж: ${playerName(state, player)} — ${SUITS[suit].adj} (+${SUITS[suit].marriage}).`,
      'marriage',
      player
    );
    return state;
  }

  function cancelMarriage(state, player) {
    if (state.pendingMarriage && state.pendingMarriage.player === player) {
      state.pendingMarriage = null;
    }
    return state;
  }

  function trickWinner(trick, trump) {
    const led = suitOf(trick[0].card);
    let pool = trick;
    if (trump) {
      const trumps = trick.filter((t) => suitOf(t.card) === trump);
      if (trumps.length) pool = trumps;
      else pool = trick.filter((t) => suitOf(t.card) === led);
    } else {
      pool = trick.filter((t) => suitOf(t.card) === led);
    }
    let best = pool[0];
    for (const item of pool) {
      if (cardPower(item.card) > cardPower(best.card)) best = item;
    }
    return best.player;
  }

  function playCard(state, player, cardId) {
    if (state.phase !== 'playing') throw new Error('Сейчас не розыгрыш');
    if (state.turn !== player) throw new Error('Не ваш ход');
    if (state.trickComplete) throw new Error('Сначала завершите взятку');
    const hand = state.hands[player];
    if (!hand.includes(cardId)) throw new Error('Этой карты нет на руке');
    const pending = state.pendingMarriage;
    if (pending && pending.player === player) {
      const suit = pending.suit;
      if (suitOf(cardId) !== suit || (rankOf(cardId) !== 'K' && rankOf(cardId) !== 'Q')) {
        throw new Error('Закрывайте марьяж королём или дамой этой масти');
      }
    }
    if (!isLegal(state, player, cardId)) throw new Error('Так ходить нельзя');
    if (pending && pending.player === player) {
      state.trump = pending.suit;
      state.marriages.push({ player, suit: pending.suit, value: pending.value });
      state.declared[player].push(pending.suit);
      pushLog(
        state,
        `Козырь — ${SUITS[pending.suit].name} (${SUITS[pending.suit].symbol}).`,
        'trump',
        player
      );
      state.pendingMarriage = null;
    }
    state.hands[player] = hand.filter((id) => id !== cardId);
    state.trick.push({ player, card: cardId });
    state.playedCards.push(cardId);
    pushLog(state, `${playerName(state, player)}: ${cardName(cardId)}`, 'play', player);

    if (state.trick.length === PLAYER_COUNT) {
      const winner = trickWinner(state.trick, state.trump);
      const points = state.trick.reduce((acc, t) => acc + cardValue(t.card), 0);
      const cards = state.trick.map((t) => t.card);
      state.tricksWon[winner].push(...cards);
      state.trickCounts[winner] += 1;
      state.stats.tricksPlayed += 1;
      state.lastTrick = {
        winner,
        cards: state.trick.slice(),
        points,
        number: state.trickNumber,
        leader: state.leader,
        complete: true,
      };
      state.trickComplete = true;
      state.turn = null;
      pushLog(
        state,
        `Взятка: ${playerName(state, winner)} (${points} ${pointsWord(points)}).`,
        'trick',
        winner
      );
    } else {
      state.turn = next(player);
    }
    return state;
  }

  function pointsWord(n) {
    const mod10 = n % 10;
    const mod100 = n % 100;
    if (mod10 === 1 && mod100 !== 11) return 'очко';
    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return 'очка';
    return 'очков';
  }

  function collectTrick(state) {
    if (!state.trickComplete || !state.lastTrick) throw new Error('Взятка ещё не завершена');
    const winner = state.lastTrick.winner;
    state.trickHistory.push({
      winner,
      cards: state.lastTrick.cards.slice(),
      points: state.lastTrick.points,
      number: state.lastTrick.number,
      leader: state.lastTrick.leader,
    });
    state.trick = [];
    state.trickNumber += 1;
    state.leader = winner;
    state.turn = winner;
    state.trickComplete = false;
    state.lastTrick = null;
    const empty = state.hands.every((hand) => hand.length === 0);
    if (empty) {
      state.phase = 'scoring';
      state.turn = null;
      computeRoundResult(state);
    } else {
      state.phase = 'playing';
    }
    return state;
  }

  function computeRoundResult(state) {
    const rows = [];
    const contract = state.contract;
    const bidder = state.bidder;
    for (let p = 0; p < PLAYER_COUNT; p += 1) {
      const cards = state.tricksWon[p];
      const trickPoints = cards.reduce((acc, c) => acc + cardValue(c), 0);
      const rounded = round5(trickPoints);
      const trickCount = state.trickCounts[p];
      const marriagePoints = state.marriages
        .filter((m) => m.player === p)
        .reduce((acc, m) => acc + m.value, 0);
      const isBidder = p === bidder;
      const success = isBidder ? trickPoints + marriagePoints >= contract : false;
      let delta = 0;
      if (isBidder) delta = success ? contract : -contract;
      else if (trickCount > 0) delta = rounded + marriagePoints;
      let boltAdded = false;
      let boltPenalty = false;
      if (trickCount === 0) {
        boltAdded = true;
        state.players[p].bolts += 1;
        if (state.players[p].bolts >= 3) {
          state.players[p].bolts = 0;
          delta -= 120;
          boltPenalty = true;
          pushLog(state, `${state.players[p].name}: третий болт, −120.`, 'score', p);
        }
      }
      state.players[p].score += delta;
      rows.push({
        player: p,
        name: state.players[p].name,
        isBidder,
        tricks: trickCount,
        trickPoints,
        rounded,
        marriagePoints,
        contract: isBidder ? contract : null,
        success,
        delta,
        boltAdded,
        boltPenalty,
        bolts: state.players[p].bolts,
        score: state.players[p].score,
        marriageList: state.marriages.filter((m) => m.player === p).map((m) => ({ ...m, suitName: SUITS[m.suit].name })),
      });
    }
    const top = Math.max(...state.players.map((p) => p.score));
    const leaders = state.players.filter((p) => p.score === top);
    const reached = top >= 1000;
    if (reached) {
      if (leaders.length === 1) state.gameWinner = leaders[0].id;
      else state.gameTie = true;
    }
    state.roundResult = {
      round: state.round,
      bidder,
      contract,
      success: rows[bidder].success,
      talonPublic: state.talonPublic,
      rows,
      top,
      gameWinner: state.gameWinner,
      tie: state.gameTie,
    };
    state.phase = 'scoring';
    const summary = rows
      .map((r) => `${r.name} ${r.delta >= 0 ? '+' : '−'}${Math.abs(r.delta)}`)
      .join(' · ');
    pushLog(state, `Итог кона: ${summary}.`, 'score');
    return state;
  }

  function nextRound(state) {
    if (state.phase !== 'scoring') throw new Error('Раунд ещё не завершён');
    if (state.gameWinner != null) {
      state.phase = 'gameover';
      return state;
    }
    state.dealer = next(state.dealer);
    startRound(state);
    return state;
  }

  function publicState(state) {
    return {
      phase: state.phase,
      round: state.round,
      dealer: state.dealer,
      bidder: state.bidder,
      contract: state.contract,
      trump: state.trump,
      turn: state.turn,
      scores: state.players.map((p) => ({ id: p.id, name: p.name, score: p.score, bolts: p.bolts })),
    };
  }

  return {
    PLAYER_COUNT,
    HAND_SIZE,
    TALON_SIZE,
    CARDS_PER_ROUND,
    RANKS,
    RANK_POWER,
    RANK_LABEL,
    CARD_VALUE,
    SUITS,
    SUIT_KEYS,
    createGame,
    startRound,
    bid,
    pass,
    takeTalon,
    discard,
    declareMarriage,
    cancelMarriage,
    legalCards,
    isLegal,
    playCard,
    collectTrick,
    nextRound,
    trickWinner,
    maxBidFor,
    marriageSuits,
    cardValue,
    cardPower,
    cardName,
    cardLongName,
    sortHand,
    round5,
    next,
    prev,
    publicState,
  };
});
