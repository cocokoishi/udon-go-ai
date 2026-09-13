import random, unittest
from Reference.go_rules_reference import *
class GoRulesTests(unittest.TestCase):
    def test_capture_and_liberty(self):
        g=GoRules(); g.board[40]=WHITE
        for i in [21,39,41]: g.board[i]=BLACK
        g.to_move=BLACK; g.history={tuple(g.board)}
        self.assertTrue(g.play(59)); self.assertEqual(g.board[40],EMPTY); self.assertEqual(g.black_captures,1)
    def test_positional_superko_rejects_immediate_recapture(self):
        g=GoRules()
        for i in [39,41,59]:g.board[i]=BLACK
        for i in [40,20,22,2]:g.board[i]=WHITE
        g.to_move=BLACK; g.history={tuple(g.board)}
        self.assertTrue(g.play(21)); self.assertEqual(g.ko_loc,40); self.assertFalse(g.play(40))
    def test_simple_ko_remains_when_positional_superko_is_disabled(self):
        g=GoRules(positional_superko=False)
        for i in [39,41,59]:g.board[i]=BLACK
        for i in [40,20,22,2]:g.board[i]=WHITE
        g.to_move=BLACK; g.history={tuple(g.board)}
        self.assertTrue(g.play(21)); self.assertEqual(g.ko_loc,40); self.assertFalse(g.play(40))
    def test_single_suicide_repeats_position_and_is_superko_illegal(self):
        g=GoRules(suicide_legal=True)
        for i in [21,39,41,59]:g.board[i]=WHITE
        g.history={tuple(g.board)}; g.to_move=BLACK; self.assertFalse(g.play(40))
    def test_multistone_suicide_can_change_position(self):
        g=GoRules(suicide_legal=True)
        for i in [40,41]: g.board[i]=BLACK
        for i in [21,39,22,42,60,58,78]:g.board[i]=WHITE
        g.to_move=BLACK; g.history={tuple(g.board)}; self.assertTrue(g.play(59))
        self.assertEqual(g.board[40],EMPTY); self.assertEqual(g.board[41],EMPTY); self.assertEqual(g.board[59],EMPTY)
        self.assertEqual(g.white_captures,3)
    def test_two_passes_finish_and_area_score(self):
        g=GoRules(komi=7.5); self.assertTrue(g.play(PASS)); self.assertTrue(g.play(PASS)); self.assertTrue(g.finished); self.assertEqual(g.winner,WHITE)
    def test_area_scoring_connected_region(self):
        g=GoRules(komi=0); g.board[1]=BLACK; g.board[19]=BLACK; self.assertEqual(g.area_score(),(361,0))
    def test_neutral_region_touches_both(self):
        g=GoRules(komi=0); g.board[39]=BLACK; g.board[41]=WHITE; g.board[21]=BLACK; g.board[59]=WHITE
        b,w=g.area_score(); self.assertEqual(b,2); self.assertEqual(w,2)
    def test_handicap(self):
        g=GoRules(); self.assertTrue(g.handicap(9)); self.assertEqual(sum(x==BLACK for x in g.board),9); self.assertEqual(g.to_move,WHITE)
    def test_resign(self):
        g=GoRules(); self.assertTrue(g.resign(BLACK)); self.assertEqual(g.winner,WHITE)
    def test_random_games_preserve_board_domain(self):
        rng=random.Random(1234)
        for _ in range(20):
            g=GoRules()
            for ply in range(500):
                if rng.random()<0.03:g.play(PASS)
                else:
                    for _attempt in range(32):
                        if g.play(rng.randrange(AREA)):break
                self.assertTrue(all(x in (-1,0,1) for x in g.board))
                if g.finished:break
if __name__=="__main__":unittest.main()
