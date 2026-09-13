from __future__ import annotations
EMPTY=0; BLACK=1; WHITE=-1; PASS=-1
SIZE=19; AREA=361
class GoRules:
    def __init__(self, komi=7.5, suicide_legal=True, positional_superko=True):
        self.board=[0]*AREA; self.to_move=BLACK; self.komi=komi; self.suicide_legal=suicide_legal
        self.positional_superko=positional_superko
        self.history={tuple(self.board)}; self.move_history=[]; self.consecutive_passes=0
        self.black_captures=0; self.white_captures=0; self.finished=False; self.winner=0; self.last_move=-2; self.ko_loc=-1
    def neigh(self,i):
        x=i%SIZE; y=i//SIZE
        if x: yield i-1
        if x<SIZE-1: yield i+1
        if y: yield i-SIZE
        if y<SIZE-1: yield i+SIZE
    def group(self,start):
        c=self.board[start]; stack=[start]; seen={start}; libs=set()
        while stack:
            q=stack.pop()
            for n in self.neigh(q):
                if self.board[n]==EMPTY: libs.add(n)
                elif self.board[n]==c and n not in seen: seen.add(n); stack.append(n)
        return seen,libs
    def play(self,loc):
        if self.finished:return False
        pla=self.to_move
        if loc==PASS:
            self.move_history.append((pla,PASS)); self.to_move=-pla; self.consecutive_passes+=1; self.last_move=PASS; self.ko_loc=-1
            if self.consecutive_passes>=2:self.finish_score()
            return True
        if loc==self.ko_loc:
            return False
        if not (0<=loc<AREA) or self.board[loc]!=EMPTY:return False
        old=self.board[:]; old_caps=(self.black_captures,self.white_captures); old_ko=self.ko_loc
        self.board[loc]=pla; captured=[]; checked=set()
        for n in self.neigh(loc):
            if self.board[n]==-pla and n not in checked:
                g,libs=self.group(n); checked|=g
                if not libs:
                    captured+=list(g)
                    for q in g:self.board[q]=EMPTY
        own,libs=self.group(loc); suicide=[]
        if not libs:
            # KataGo's multi-stone-suicide flag does not make a singleton
            # suicide legal; only a connected group of at least two stones
            # may be removed when the rules allow multi-stone suicide.
            if len(own)==1 or not self.suicide_legal:self.board=old; return False
            suicide=list(own)
            for q in own:self.board[q]=EMPTY
        key=tuple(self.board)
        if self.positional_superko and key in self.history:
            self.board=old; self.black_captures,self.white_captures=old_caps; self.ko_loc=old_ko; return False
        if pla==BLACK:
            self.black_captures+=len(captured); self.white_captures+=len(suicide)
        else:
            self.white_captures+=len(captured); self.black_captures+=len(suicide)
        if self.positional_superko:self.history.add(key)
        self.move_history.append((pla,loc)); self.to_move=-pla; self.consecutive_passes=0; self.last_move=loc; self.ko_loc=-1
        if len(captured)==1 and not suicide and self.board[loc]==pla:
            own2,libs2=self.group(loc)
            if len(own2)==1 and len(libs2)==1:self.ko_loc=captured[0]
        return True
    def area_score(self):
        b=sum(1 for x in self.board if x==BLACK); w=sum(1 for x in self.board if x==WHITE); seen=set()
        for i,c in enumerate(self.board):
            if c!=EMPTY or i in seen:continue
            stack=[i]; seen.add(i); reg=[]; touches=set()
            while stack:
                q=stack.pop(); reg.append(q)
                for n in self.neigh(q):
                    c2=self.board[n]
                    if c2==EMPTY and n not in seen:seen.add(n); stack.append(n)
                    elif c2!=EMPTY:touches.add(c2)
            if touches=={BLACK}:b+=len(reg)
            elif touches=={WHITE}:w+=len(reg)
        return b,w
    def finish_score(self):
        b,w=self.area_score(); s=w+self.komi-b; self.finished=True; self.winner=WHITE if s>0 else BLACK if s<0 else EMPTY; return s
    def resign(self,pla=None):
        if self.finished:return False
        if pla is None:pla=self.to_move
        self.finished=True; self.winner=-pla; return True
    def handicap(self,n):
        if self.move_history:return False
        pts={2:[(3,15),(15,3)],3:[(3,15),(15,3),(15,15)],4:[(3,15),(15,3),(15,15),(3,3)],5:[(3,15),(15,3),(15,15),(3,3),(9,9)],6:[(3,15),(15,3),(15,15),(3,3),(3,9),(15,9)],7:[(3,15),(15,3),(15,15),(3,3),(3,9),(15,9),(9,9)],8:[(3,15),(15,3),(15,15),(3,3),(3,9),(15,9),(9,3),(9,15)],9:[(3,15),(15,3),(15,15),(3,3),(3,9),(15,9),(9,3),(9,15),(9,9)]}
        if n not in pts:return False
        for x,y in pts[n]:self.board[y*SIZE+x]=BLACK
        self.to_move=WHITE; self.history={tuple(self.board)}; return True
