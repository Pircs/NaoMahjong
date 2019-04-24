using System.Collections.Generic;
using System.Linq;
using Multi.MahjongMessages;
using Multi.ServerData;
using Single;
using Single.MahjongDataType;
using StateMachine.Interfaces;
using UnityEngine;
using UnityEngine.Networking;


namespace Multi.GameState
{
    /// <summary>
    /// When the server is in this state, the server will distribute initial tiles for every player, 
    /// and will determine the initial dora indicator(s) according to the settings.
    /// All the data such as initial tiles, initial dora indicators, and mahjongSetData.
    /// Transfers to PlayerDrawTileState. The state transfer will be done regardless whether enough client responds received.
    /// </summary>
    public class RoundStartState : IState
    {
        public ServerRoundStatus CurrentRoundStatus;
        public MahjongSet MahjongSet;
        public bool NextRound;
        public bool ExtraRound;
        public bool KeepSticks;
        private IList<Player> players;
        private ServerRoundStartMessage[] messages;
        private bool[] responds;
        private float firstSendTime;
        private float lastSendTime;
        public void OnStateEnter()
        {
            Debug.Log("Server enters RoundStartState");
            players = CurrentRoundStatus.Players;
            NetworkServer.RegisterHandler(MessageIds.ClientReadinessMessage, OnReadinessMessageReceived);
            var doraIndicators = MahjongSet.Reset();
            // throwing dice
            var dice = Random.Range(CurrentRoundStatus.GameSettings.DiceMin, CurrentRoundStatus.GameSettings.DiceMax + 1);
            CurrentRoundStatus.NextRound(dice, NextRound, ExtraRound, KeepSticks);
            // draw initial tiles
            DrawInitial();
            Debug.Log("[Server] Initial tiles distribution done");
            CurrentRoundStatus.SortHandTiles();
            messages = new ServerRoundStartMessage[players.Count];
            responds = new bool[players.Count];
            for (int index = 0; index < players.Count; index++)
            {
                var tiles = CurrentRoundStatus.HandTiles(index);
                Debug.Log($"[Server] Hand tiles of player {index}: {string.Join("", tiles)}");
                messages[index] = new ServerRoundStartMessage
                {
                    PlayerIndex = index,
                    Field = CurrentRoundStatus.Field,
                    Dice = CurrentRoundStatus.Dice,
                    Extra = CurrentRoundStatus.Extra,
                    RichiSticks = CurrentRoundStatus.RichiSticks,
                    OyaPlayerIndex = CurrentRoundStatus.OyaPlayerIndex,
                    Points = CurrentRoundStatus.Points.ToArray(),
                    InitialHandTiles = tiles,
                    MahjongSetData = MahjongSet.Data
                };
                players[index].connectionToClient.Send(MessageIds.ServerRoundStartMessage, messages[index]);
            }
            firstSendTime = Time.time;
            lastSendTime = Time.time;
        }

        public void OnStateUpdate()
        {
            if (responds.All(r => r) || Time.time - firstSendTime >= ServerConstants.ServerTimeOut)
            {
                ServerNextState();
                return;
            }
            if (Time.time - lastSendTime >= ServerConstants.MessageResendInterval)
            {
                lastSendTime = Time.time;
                for (int i = 0; i < players.Count; i++)
                {
                    if (responds[i]) continue;
                    players[i].connectionToClient.Send(MessageIds.ServerRoundStartMessage, messages[i]);
                }
            }
        }

        private void ServerNextState()
        {
            ServerBehaviour.Instance.DrawTile(CurrentRoundStatus.OyaPlayerIndex);
        }

        private void OnReadinessMessageReceived(NetworkMessage message)
        {
            var content = message.ReadMessage<ClientReadinessMessage>();
            Debug.Log($"Received ClientReadinessMessage: {content}");
            if (content.Content != MahjongConstants.CompleteHandTilesCount)
            {
                Debug.LogError("Something is wrong, the received readiness meassage contains invalid content");
                return;
            }
            responds[content.PlayerIndex] = true;
        }

        public void OnStateExit()
        {
            Debug.Log("Server exits RoundStartState");
            NetworkServer.UnregisterHandler(MessageIds.ClientReadinessMessage);
        }

        private void DrawInitial()
        {
            for (int round = 0; round < CurrentRoundStatus.GameSettings.InitialDrawRound; round++)
            {
                // Draw 4 tiles for each player
                for (int index = 0; index < players.Count; index++)
                {
                    for (int i = 0; i < CurrentRoundStatus.GameSettings.TilesEveryRound; i++)
                    {
                        var tile = MahjongSet.DrawTile();
                        CurrentRoundStatus.AddTile(index, tile);
                    }
                }
            }
            // Last round, 1 tile for each player
            for (int index = 0; index < players.Count; index++)
            {
                for (int i = 0; i < CurrentRoundStatus.GameSettings.TilesLastRound; i++)
                {
                    var tile = MahjongSet.DrawTile();
                    CurrentRoundStatus.AddTile(index, tile);
                }
            }
        }
    }
}