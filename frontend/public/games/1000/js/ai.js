/*!
 * «1000» — ИИ соперников.
 * Торги, снос, объявление марьяжей и розыгрыш взяток.
 */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory(require('./engine'));
  else root.ThousandAI = factory(root.ThousandEngine);
})(typeof globalThis !== 'undefined' ? globalThis : this, function (E) {
  'use strict';

  const { SUITS, SUIT_KEYS, CARD_VALUE, RANK_POWER } = E;

  const PERSONALITY = {
    1: { aggression: 0.94, targetPass: 0.28, bluff: 0.04, name: 'Ворон' },
    2: { aggression: 1.07, targetPass: 0.12, bluff: 0.17, name: 'Лиса' },
  };

  function suitOf(card) {
    return card[0];
  }

  function rankOf(card) {
    return card.slice(1);
  }

  function value(card) {
    return CARD_VALUE[rankOf(card)] || 0;
  }

  function countSuit(hand, suit) {
    let n = 0;
    for (const c of hand) if (suitOf(c) === suit) n += 1;
    return n;
  }

  function has(hand, rank, suit) {
    return hand.includes(suit + rank);
  }

  function isMarriageCard(hand, card) {
    const r = rankOf(card);
    if (r !== 'K' && r !== 'Q') return false;
    const s = suitOf(card);
    return has(hand, 'K', s) && has(hand, 'Q', s);
  }

  function personality(p) {
    return PERSONALITY[p] || { aggression: 1, targetPass: 0.2, bluff: 0.1, name: 'Бот' };
  }

  function round5(n) {
    return Math.round(n / 5) * 5;
  }

  function clamp(n, min, max) {
    return Math.max(min, Math.min(max, n));
  }

  /**
   * Оценка силы руки: сколько очков реально можно собрать.
   * Это не «максимум по правилам», а ожидаемый результат.
   */
  function evaluateHand(state, p) {
    const hand = state.hands[p];
    const marriages = E.marriageSuits(hand);
    const marriageSum = marriages.reduce((acc, s) => acc + SUITS[s].marriage, 0);
    let expected = marriageSum * 0.97;
    let aces = 0;
    let tens = 0;
    let kings = 0;
    let queens = 0;

    for (const card of hand) {
      const r = rankOf(card);
      if (r === 'A') {
        aces += 1;
        expected += 9.2;
      } else if (r === '10') {
        tens += 1;
        expected += 5.2;
      } else if (r === 'K') {
        kings += 1;
        expected += 1.7;
      } else if (r === 'Q') {
        queens += 1;
        expected += 0.8;
      } else if (r === 'J') {
        expected += 0.2;
      }
    }

    for (const s of SUIT_KEYS) {
      const len = countSuit(hand, s);
      if (len >= 4) expected += 1.4;
      if (has(hand, 'A', s) && has(hand, '10', s)) expected += 2.4;
      if (len === 1) expected -= 0.6;
      if (len === 0) expected += 0.8;
    }

    for (const s of marriages) {
      expected += Math.max(0, countSuit(hand, s) - 2) * 1.1;
    }

    // Марьяж даёт контроль над козырем и первый заход — это стоит дороже,
    // чем просто сумма номиналов.
    if (marriageSum > 0) expected += Math.min(9, marriageSum * 0.08);

    if (aces === 0) expected -= 7;
    if (tens === 0) expected -= 3;
    if (kings + queens === 0) expected -= 2;

    return { expected, marriageSum, marriages, aces, tens, kings, queens };
  }

  function chooseBid(state, p, rng) {
    if (state.phase !== 'bidding' || !state.auction || state.auction.turn !== p) return 'pass';
    const auction = state.auction;
    const maxBid = E.maxBidFor(state, p);
    const minBid = auction.high === 0 ? 100 : auction.high + 5;
    const persona = personality(p);

    if (minBid > maxBid) return 'pass';
    if (auction.high === 0) return 100; // первая рука обязана заявить сотню

    const { expected } = evaluateHand(state, p);
    let target = clamp(round5(expected * persona.aggression), 100, maxBid);

    if (target < minBid) {
      const close = minBid - target <= 10;
      if (close && rng() < persona.bluff) return minBid;
      return 'pass';
    }

    if (target === minBid && rng() < persona.targetPass) return 'pass';

    let amount = minBid;
    if (target - minBid >= 20 && rng() < 0.28) amount = minBid + (rng() < 0.45 ? 10 : 5);
    return clamp(amount, minBid, maxBid);
  }

  function keepValue(hand, card, marriages) {
    const r = rankOf(card);
    const s = suitOf(card);
    let v = value(card) * 1.35;
    if (r === 'A') v += 15;
    else if (r === '10') v += 11;
    else if (r === 'K') v += 2;
    else if (r === 'Q') v += 1;
    else if (r === '9') v -= 2.5;
    v += countSuit(hand, s) * 0.9;
    if ((r === 'K' || r === 'Q') && marriages.includes(s)) {
      v += SUITS[s].marriage * 1.15;
    }
    return v;
  }

  function chooseDiscard(state, p) {
    const hand = state.hands[p].slice();
    const marriages = E.marriageSuits(hand);
    const ranked = hand
      .map((card) => ({ card, keep: keepValue(hand, card, marriages) }))
      .sort((a, b) => a.keep - b.keep);
    const chosen = ranked.slice(0, 2);
    const opponents = [E.next(p), E.next(E.next(p))];
    const strong = opponents.slice().sort((a, b) => state.players[b].score - state.players[a].score)[0];
    const weak = opponents.find((x) => x !== strong);
    // более слабую карту — более сильному сопернику
    const ordered = chosen.slice().sort((a, b) => a.keep - b.keep);
    return {
      cardIds: ordered.map((x) => x.card),
      recipients: [strong, weak],
    };
  }

  function isVoidIn(state, p, suit) {
    for (const trick of state.trickHistory) {
      const led = suitOf(trick.cards[0].card);
      if (led !== suit) continue;
      const play = trick.cards.find((x) => x.player === p);
      if (play && suitOf(play.card) !== suit) return true;
    }
    if (state.trick.length) {
      const led = suitOf(state.trick[0].card);
      if (led === suit) {
        const play = state.trick.find((x) => x.player === p);
        if (play && suitOf(play.card) !== suit) return true;
      }
    }
    return false;
  }

  function availableMarriages(state, p) {
    const hand = state.hands[p];
    return E.marriageSuits(hand)
      .filter((s) => !state.declared[p].includes(s))
      .map((s) => ({
        suit: s,
        value: SUITS[s].marriage,
        length: countSuit(hand, s),
      }));
  }

  function chooseMarriage(state, p) {
    if (state.phase !== 'playing' || state.turn !== p) return null;
    if (state.trick.length || state.trickNumber < 1 || state.pendingMarriage) return null;
    const options = availableMarriages(state, p);
    if (!options.length) return null;
    const hand = state.hands[p];
    const trump = state.trump;
    const trumpLen = trump ? countSuit(hand, trump) : 0;

    if (!trump) {
      options.sort((a, b) => b.value + b.length * 6 - (a.value + a.length * 6));
      return options[0].suit;
    }

    const same = options.find((o) => o.suit === trump);
    if (same) return same.suit;

    let best = null;
    let bestScore = -Infinity;
    for (const opt of options) {
      const score = opt.value + opt.length * 8 - trumpLen * 7;
      if (score > bestScore) {
        best = opt;
        bestScore = score;
      }
    }
    if (best && bestScore > 14 && (best.value >= 80 || best.length >= 5) && trumpLen <= 3) {
      return best.suit;
    }
    return null;
  }

  function leadScore(state, p, card) {
    const hand = state.hands[p];
    const s = suitOf(card);
    const r = rankOf(card);
    const trump = state.trump;
    let score = 0;

    if (r === 'A') score += 16;
    else if (r === '10') score += 10;
    else if (r === 'K') score += 4;
    else if (r === 'Q') score += 2;
    else if (r === 'J') score += 0.2;
    else if (r === '9') score -= 5;

    score += countSuit(hand, s) * 0.35;

    if (trump && s === trump) {
      score += 2 + countSuit(hand, trump) * 0.5;
      if (!has(hand, 'A', trump) && !has(hand, '10', trump)) score -= 4;
    }

    if (trump && s !== trump) {
      let voidCount = 0;
      for (const opp of [E.next(p), E.next(E.next(p))]) {
        if (isVoidIn(state, opp, s)) voidCount += 1;
      }
      score -= voidCount * 7;
      if (countSuit(hand, s) === 1) score -= 4;
    }

    if ((r === 'K' || r === 'Q') && isMarriageCard(hand, card) && !state.declared[p].includes(s)) {
      score -= 13;
    }

    if (r === 'A' && has(hand, '10', s)) score += 2.5;
    return score;
  }

  function followScore(state, p, card) {
    const hand = state.hands[p];
    const s = suitOf(card);
    const r = rankOf(card);
    const trump = state.trump;
    const sim = state.trick.concat([{ player: p, card }]);
    const winner = E.trickWinner(sim, trump);
    const weWin = winner === p;
    const tablePoints = state.trick.reduce((acc, t) => acc + value(t.card), 0);
    const totalPoints = tablePoints + value(card);
    const remaining = 3 - sim.length;
    const isTrump = Boolean(trump && s === trump);
    let score = 0;

    if (weWin) {
      score += 8 + totalPoints * 0.5;
      score -= value(card) * 0.55;
      if (isTrump) score -= 3.2;
      if (remaining > 0) score -= 3;
      if (state.trickCounts[p] === 0) score += 5;
      if (value(card) >= 10 && totalPoints < 9) score -= 6;
    } else {
      score -= value(card) * 1.15;
      if (r === '9') score += 5;
      else if (r === 'J') score += 2;
      if (isTrump) score -= 4;
    }

    if ((r === 'K' || r === 'Q') && isMarriageCard(hand, card) && !state.declared[p].includes(s)) {
      score -= 8;
    }

    if (remaining === 0) {
      if (weWin) score += 3 + totalPoints * 0.2;
      else score += 1;
    }
    return score;
  }

  function chooseLead(state, p, rng) {
    const legal = E.legalCards(state, p);
    let best = legal[0];
    let bestScore = -Infinity;
    for (const card of legal) {
      const score = leadScore(state, p, card) + (rng() - 0.5) * 1.6;
      if (score > bestScore) {
        bestScore = score;
        best = card;
      }
    }
    return best;
  }

  function chooseFollow(state, p, rng) {
    const legal = E.legalCards(state, p);
    let best = legal[0];
    let bestScore = -Infinity;
    for (const card of legal) {
      const score = followScore(state, p, card) + (rng() - 0.5) * 1.2;
      if (score > bestScore) {
        bestScore = score;
        best = card;
      }
    }
    return best;
  }

  function chooseCard(state, p, rng) {
    if (state.phase !== 'playing' || state.turn !== p) return null;
    const legal = E.legalCards(state, p);
    if (!legal.length) return null;
    if (legal.length === 1) return legal[0];

    if (state.pendingMarriage && state.pendingMarriage.player === p) {
      // марьяж уже объявлен — заходим дамой, сохраняя короля
      const women = legal.filter((c) => rankOf(c) === 'Q');
      return (women[0] || legal[0]);
    }

    if (!state.trick.length) return chooseLead(state, p, rng);
    return chooseFollow(state, p, rng);
  }

  return {
    PERSONALITY,
    evaluateHand,
    chooseBid,
    chooseDiscard,
    chooseMarriage,
    chooseCard,
    availableMarriages,
    isVoidIn,
  };
});
