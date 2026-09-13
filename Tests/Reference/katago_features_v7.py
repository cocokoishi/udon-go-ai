"""Reference for the V7 input planes used by the local fixture tests.

Indices are transcribed from the bundled
``.KataGO/KataGo-master/cpp/neuralnet/nninputs.cpp`` ``fillRowV7`` function.
The production encoder's ladder planes are exercised by the executed Unity
versus KataGo differential corpus; this compact Python reference leaves those
planes at zero because it is intended for the small hand-authored fixtures.
"""
from __future__ import annotations

import math

SIZE = 19
AREA = SIZE * SIZE
SPATIAL_CHANNELS = 22
GLOBAL_CHANNELS = 19
EMPTY = 0
BLACK = 1
WHITE = -1
PASS = -1
NONE = -2
PLAYING = 0


def neighbours(loc: int):
    x, y = loc % SIZE, loc // SIZE
    if x > 0:
        yield loc - 1
    if x < SIZE - 1:
        yield loc + 1
    if y > 0:
        yield loc - SIZE
    if y < SIZE - 1:
        yield loc + SIZE


def group_and_liberties(board: list[int], start: int):
    color = board[start]
    stack = [start]
    stones = {start}
    liberties = set()
    while stack:
        loc = stack.pop()
        for n in neighbours(loc):
            if board[n] == EMPTY:
                liberties.add(n)
            elif board[n] == color and n not in stones:
                stones.add(n)
                stack.append(n)
    return stones, liberties


def area_owner(board: list[int]) -> list[int]:
    owner = board[:]
    visited = set()
    for start, value in enumerate(board):
        if value != EMPTY or start in visited:
            continue
        stack = [start]
        visited.add(start)
        region = []
        touches = set()
        while stack:
            loc = stack.pop()
            region.append(loc)
            for n in neighbours(loc):
                if board[n] == EMPTY and n not in visited:
                    visited.add(n)
                    stack.append(n)
                elif board[n] != EMPTY:
                    touches.add(board[n])
        region_owner = next(iter(touches)) if len(touches) == 1 else EMPTY
        for loc in region:
            owner[loc] = region_owner
    return owner


def _self_komi(komi_times2: int, pla: int) -> float:
    value = (komi_times2 * 0.5) if pla == WHITE else -(komi_times2 * 0.5)
    return max(-AREA - 20.0, min(AREA + 20.0, value))


def _parity_wave(self_komi: float) -> float:
    komi_floor = math.floor((self_komi - 1.0) / 2.0) * 2.0 + 1.0
    delta = max(0.0, min(2.0, self_komi - komi_floor))
    if delta < 0.5:
        return delta
    if delta < 1.5:
        return 1.0 - delta
    return delta - 2.0


def encode(
    board: list[int],
    previous: list[int] | None,
    previous_previous: list[int] | None,
    recent_locs: list[int],
    recent_pla: list[int],
    side_to_move: int,
    ko_loc: int,
    komi_times2: int,
    positional_superko: bool,
    multi_stone_suicide_legal: bool,
    area_scoring: bool,
    consecutive_passes: int,
    game_state: int = PLAYING,
    superko_banned: list[bool] | None = None,
) -> tuple[list[float], list[float]]:
    if len(board) != AREA:
        raise ValueError("board must contain 361 points")
    if side_to_move not in (BLACK, WHITE):
        raise ValueError("invalid side to move")
    spatial = [0.0] * (SPATIAL_CHANNELS * AREA)
    global_features = [0.0] * GLOBAL_CHANNELS
    pla = side_to_move

    for loc, color in enumerate(board):
        spatial[loc] = 1.0
        if color == EMPTY:
            continue
        stones, liberties = group_and_liberties(board, loc)
        if loc != min(stones):
            continue
        plane = 1 if color == pla else 2
        for stone in stones:
            spatial[plane * AREA + stone] = 1.0
            if len(liberties) == 1:
                spatial[3 * AREA + stone] = 1.0
            elif len(liberties) == 2:
                spatial[4 * AREA + stone] = 1.0
            elif len(liberties) == 3:
                spatial[5 * AREA + stone] = 1.0

    if 0 <= ko_loc < AREA:
        spatial[6 * AREA + ko_loc] = 1.0
    if positional_superko and superko_banned is not None:
        for loc, banned in enumerate(superko_banned[:AREA]):
            if banned and loc != ko_loc:
                spatial[6 * AREA + loc] = 1.0

    # KataGo includes the latest pass after an actually finished game. A
    # hypothetical pass suppression is a separate search-time condition and
    # is not represented by this production state API.
    history_limit = 5 if game_state == PLAYING else 1
    history_count = 0
    for i, (loc, color) in enumerate(zip(recent_locs[:history_limit], recent_pla[:history_limit])):
        expected = -pla if i % 2 == 0 else pla
        if color != expected:
            break
        history_count = i + 1
        if loc == PASS:
            global_features[i] = 1.0
        elif 0 <= loc < AREA:
            spatial[(9 + i) * AREA + loc] = 1.0

    # Planes 14-17 are supplied by the bundled ladder search in production.
    _ = history_count, previous, previous_previous

    if area_scoring:
        owners = area_owner(board)
        for loc, owner in enumerate(owners):
            if owner == pla:
                spatial[18 * AREA + loc] = 1.0
            elif owner == -pla:
                spatial[19 * AREA + loc] = 1.0

    self_komi = _self_komi(komi_times2, pla)
    global_features[5] = self_komi / 20.0
    if positional_superko:
        global_features[6] = 1.0
        global_features[7] = 0.5
    if multi_stone_suicide_legal:
        global_features[8] = 1.0
    if not area_scoring:
        global_features[9] = 1.0
    global_features[14] = 1.0 if consecutive_passes > 0 else 0.0
    global_features[18] = _parity_wave(self_komi)
    return spatial, global_features
