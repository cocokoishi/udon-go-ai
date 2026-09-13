import unittest

from Reference.katago_features_v7 import AREA, BLACK, EMPTY, NONE, PASS, WHITE, encode


class KataGoV7FeatureTests(unittest.TestCase):
    def setUp(self):
        self.board = [EMPTY] * AREA
        self.previous = [EMPTY] * AREA
        self.previous_previous = [EMPTY] * AREA
        self.recent_locs = [NONE] * 5
        self.recent_pla = [EMPTY] * 5
        self.banned = [False] * AREA

    def test_empty_board_matches_v7_global_contract(self):
        spatial, global_features = encode(
            self.board, self.previous, self.previous_previous,
            self.recent_locs, self.recent_pla, BLACK, NONE, 15,
            True, True, True, 0, superko_banned=self.banned,
        )
        self.assertEqual(len(spatial), 22 * AREA)
        self.assertEqual(len(global_features), 19)
        self.assertTrue(all(value == 1.0 for value in spatial[:AREA]))
        self.assertEqual(sum(spatial[18 * AREA : 20 * AREA]), 0.0)
        self.assertAlmostEqual(global_features[5], -0.375)
        self.assertAlmostEqual(global_features[6], 1.0)
        self.assertAlmostEqual(global_features[7], 0.5)
        self.assertAlmostEqual(global_features[8], 1.0)
        self.assertAlmostEqual(global_features[18], -0.5)

    def test_perspective_liberties_area_history_and_ko(self):
        self.board[9] = BLACK
        self.recent_locs[0] = 180
        self.recent_pla[0] = BLACK
        self.recent_locs[1] = PASS
        self.recent_pla[1] = WHITE
        self.banned[10] = True
        spatial, global_features = encode(
            self.board, self.previous, self.previous_previous,
            self.recent_locs, self.recent_pla, WHITE, 11, 15,
            True, True, True, 1, superko_banned=self.banned,
        )
        self.assertEqual(spatial[2 * AREA + 9], 1.0)
        self.assertEqual(spatial[5 * AREA + 9], 1.0)
        self.assertEqual(spatial[9 * AREA + 180], 1.0)
        self.assertEqual(spatial[6 * AREA + 10], 1.0)
        self.assertEqual(spatial[6 * AREA + 11], 1.0)
        self.assertEqual(global_features[1], 1.0)
        self.assertAlmostEqual(global_features[5], 0.375)
        self.assertAlmostEqual(global_features[18], 0.5)
        self.assertTrue(all(spatial[19 * AREA + i] == 1.0 for i in range(AREA)))

    def test_territory_mode_omits_pre_encore_area_planes(self):
        self.board[9] = BLACK
        spatial, global_features = encode(
            self.board, self.previous, self.previous_previous,
            self.recent_locs, self.recent_pla, WHITE, NONE, 15,
            True, True, False, 0, superko_banned=self.banned,
        )
        self.assertEqual(global_features[9], 1.0)
        self.assertEqual(sum(spatial[18 * AREA : 20 * AREA]), 0.0)

    def test_terminal_state_keeps_only_latest_pass_history(self):
        recent_locs = [PASS, PASS, NONE, NONE, NONE]
        recent_pla = [WHITE, BLACK, EMPTY, EMPTY, EMPTY]
        spatial, global_features = encode(
            self.board, self.previous, self.previous_previous,
            recent_locs, recent_pla, BLACK, NONE, 15,
            True, True, True, 2, game_state=1,
            superko_banned=self.banned,
        )
        self.assertEqual(global_features[0], 1.0)
        self.assertEqual(global_features[1], 0.0)
        self.assertEqual(sum(spatial[9 * AREA : 14 * AREA]), 0.0)


if __name__ == "__main__":
    unittest.main()
