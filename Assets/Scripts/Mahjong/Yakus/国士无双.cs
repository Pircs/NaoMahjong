﻿using Mahjong.YakuUtils;

namespace Mahjong.Yakus
{
    public class 国士无双 : Yaku
    {
        private static readonly string normal = "国士无双";
        private static readonly string shisan = "国士无双·十三面听";
        private string name = normal;
        private int value = YakuUtil.YakuManBasePoint;
        public override string Name => name;
        public override int Value => value;
        public override bool IsYakuMan => true;
        public override YakuType Type => YakuType.Menqian;

        public override bool Test(MianziSet hand, Tile rong, GameStatus status, params YakuOption[] options)
        {
            if (hand.MianziCount != 13) return false;
            bool isShisan = false;
            foreach (var mianzi in hand)
            {
                if (mianzi.Contains(rong) && mianzi.Type == MianziType.Jiang)
                {
                    isShisan = true;
                    break;
                }
            }

            if (isShisan)
            {
                name = shisan;
                value = YakuUtil.YakuManBasePoint + 1;
            }
            else
            {
                name = normal;
                value = YakuUtil.YakuManBasePoint;
            }
            return true;
        }
    }
}