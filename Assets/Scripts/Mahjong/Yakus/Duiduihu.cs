﻿namespace Mahjong.Yakus
{
    public class Duiduihu : Yaku
    {
        public override string Name
        {
            get { return "对对和"; }
        }

        public override int Value
        {
            get { return 2; }
        }

        public override bool Test(MianziSet hand, Tile rong, GameStatus status, params YakuOption[] options)
        {
            int count = 0;
            foreach (var mianzi in hand)
            {
                if (mianzi.Type == MianziType.Shunzi || mianzi.Type == MianziType.Single) return false;
                if (mianzi.Type == MianziType.Kezi) count++;
            }

            return count == 4;
        }
    }
}