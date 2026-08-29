deck = "Deck"
discardPile = "Discard Pile"
highlight = "#ff0000"
drawManyDefault = 5


def shuffle(group, x=0, y=0):
    mute()
    group.shuffle()
    notify("{} shuffles their {}.".format(me, group.name))


def draw(group, x=0, y=0):
    mute()
    if len(group) < 1:
        return
    card = group.top()
    card.moveTo(card.owner.hand)
    notify("{} draws a card from {}.".format(me, group.name))


def drawMany(group, x=0, y=0):
    if len(group) < 1:
        return
    mute()
    global drawManyDefault
    count = askInteger("Draw how many cards?", drawManyDefault)
    if count is None or count < 1:
        return
    drawManyDefault = count
    for card in group.top(count):
        card.moveTo(card.owner.hand)
    notify("{} draws {} cards from {}.".format(me, count, group.name))


def randomDiscard(group, x=0, y=0):
    mute()
    card = group.random()
    if card is None:
        return
    card.moveTo(me.piles[discardPile])
    notify("{} randomly discards {} from {}.".format(me, card, group.name))


def rollDice(group, x=0, y=0):
    mute()
    notify("{} rolls {} on a 6-sided die.".format(me, rnd(1, 6)))


def flipCoin(group, x=0, y=0):
    mute()
    notify("{} flips {}.".format(me, "heads" if rnd(1, 2) == 1 else "tails"))


def passTurn(group, x=0, y=0):
    notify("{} passes.".format(me))


def rotate(cards, x=0, y=0):
    mute()
    for card in cards:
        card.orientation ^= Rot90
        if card.orientation & Rot90 == Rot90:
            notify("{} turns {} sideways".format(me, card))
        else:
            notify("{} turns {} upright".format(me, card))


def flip(cards, x=0, y=0):
    mute()
    for card in cards:
        card.isFaceUp = not card.isFaceUp
        notify("{} flips {} face {}.".format(me, card, "up" if card.isFaceUp else "down"))


def highlightCard(cards, x=0, y=0):
    mute()
    for card in cards:
        if card.highlight == highlight:
            card.highlight = None
            notify("{} removes highlight from {}".format(me, card))
        else:
            card.highlight = highlight
            notify("{} highlights {}".format(me, card))


def discard(card, x=0, y=0):
    mute()
    card.moveTo(card.owner.piles[discardPile])
    notify("{} discards {}.".format(me, card))
