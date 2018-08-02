﻿using System.Linq;
using Mahjong.YakuUtils;

namespace Mahjong.Yakus
{
    public class 三色同顺 : Yaku
    {
        private int value = 2;

        public override string Name => "三色同顺";

        public override int Value => value;

        public override YakuType Type => YakuType.Shixia;

        public override bool Test(MianziSet hand, Tile rong, GameStatus status, params YakuOption[] options)
        {
            value = options.Contains(YakuOption.Menqing) ? 2 : 1;
            var suitFlag = new int[9];
            foreach (var mianzi in hand)
            {
                if (mianzi.Type == MianziType.Shunzi)
                    suitFlag[mianzi.First.Index - 1] |= 1 << (int) mianzi.Suit; // binary: M = 1, P = 10, S = 100
            }

            for (int i = 0; i < 9; i++)
            {
                if (suitFlag[i] == 7) return true; // (binary) 111 = (decimal) 7
            }

            return false;
        }
    }
}