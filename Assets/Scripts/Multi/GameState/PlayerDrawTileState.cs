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
    // todo -- draw lingshang tile
    public class PlayerDrawTileState : IState
    {
        public int CurrentPlayerIndex;
        public MahjongSet MahjongSet;
        public ServerRoundStatus CurrentRoundStatus;
        public bool IsLingShang;
        public bool TurnDoraAfterDiscard;
        private GameSettings gameSettings;
        private YakuSettings yakuSettings;
        private IList<Player> players;
        private Tile justDraw;
        private MessageBase[] messages;
        private bool[] responds;
        private float lastSendTime;
        private float firstSendTime;
        private float serverTimeOut;

        public void OnStateEnter()
        {
            Debug.Log($"Server enters {GetType().Name}");
            gameSettings = CurrentRoundStatus.GameSettings;
            yakuSettings = CurrentRoundStatus.YakuSettings;
            players = CurrentRoundStatus.Players;
            NetworkServer.RegisterHandler(MessageIds.ClientReadinessMessage, OnReadinessMessageReceived);
            NetworkServer.RegisterHandler(MessageIds.ClientInTurnOperationMessage, OnInTurnOperationReceived);
            NetworkServer.RegisterHandler(MessageIds.ClientDiscardTileMessage, OnDiscardTileReceived);
            NetworkServer.RegisterHandler(MessageIds.ClientNineOrphansMessage, OnClientRoundDrawMessageReceived);
            if (IsLingShang)
                justDraw = MahjongSet.DrawLingShang();
            else
                justDraw = MahjongSet.DrawTile();
            CurrentRoundStatus.CurrentPlayerIndex = CurrentPlayerIndex;
            CurrentRoundStatus.LastDraw = justDraw;
            CurrentRoundStatus.CheckFirstTurn(CurrentPlayerIndex);
            Debug.Log($"[Server] Distribute a tile {justDraw} to current turn player {CurrentPlayerIndex}, "
                + $"first turn: {CurrentRoundStatus.FirstTurn}.");
            messages = new MessageBase[players.Count];
            responds = new bool[players.Count];
            for (int i = 0; i < players.Count; i++)
            {
                if (i == CurrentPlayerIndex) continue;
                messages[i] = new ServerOtherDrawTileMessage
                {
                    PlayerIndex = i,
                    CurrentTurnPlayerIndex = CurrentPlayerIndex,
                    MahjongSetData = MahjongSet.Data
                };
                players[i].connectionToClient.Send(MessageIds.ServerOtherDrawTileMessage, messages[i]);
            }
            messages[CurrentPlayerIndex] = new ServerDrawTileMessage
            {
                PlayerIndex = CurrentPlayerIndex,
                Tile = justDraw,
                BonusTurnTime = players[CurrentPlayerIndex].BonusTurnTime,
                Richied = CurrentRoundStatus.RichiStatus(CurrentPlayerIndex),
                Operations = GetOperations(CurrentPlayerIndex),
                MahjongSetData = MahjongSet.Data
            };
            players[CurrentPlayerIndex].connectionToClient.Send(MessageIds.ServerDrawTileMessage, messages[CurrentPlayerIndex]);
            firstSendTime = Time.time;
            lastSendTime = Time.time;
            serverTimeOut = gameSettings.BaseTurnTime + players[CurrentPlayerIndex].BonusTurnTime + ServerConstants.ServerTimeBuffer;
        }

        // todo -- complete this
        private InTurnOperation[] GetOperations(int playerIndex)
        {
            var operations = new List<InTurnOperation> { new InTurnOperation { Type = InTurnOperationType.Discard } };
            var point = GetTsumoInfo(playerIndex, justDraw);
            // test if enough
            if (gameSettings.CheckConstraint(point))
            {
                operations.Add(new InTurnOperation
                {
                    Type = InTurnOperationType.Tsumo,
                    Tile = justDraw
                });
            }
            var handTiles = CurrentRoundStatus.HandTiles(playerIndex);
            var openMelds = CurrentRoundStatus.Melds(playerIndex);
            // test round draw
            Test9Orphans(handTiles, operations);
            // test richi
            TestRichi(playerIndex, handTiles, openMelds, operations);
            // test kongs
            TestKongs(playerIndex, handTiles, operations);
            // test bei -- todo
            return operations.ToArray();
        }

        private void Test9Orphans(Tile[] handTiles, IList<InTurnOperation> operations)
        {
            if (!CurrentRoundStatus.FirstTurn) return;
            if (MahjongLogic.Test9KindsOfOrphans(handTiles, justDraw))
            {
                operations.Add(new InTurnOperation
                {
                    Type = InTurnOperationType.RoundDraw
                });
            }
        }

        private void TestRichi(int playerIndex, Tile[] handTiles, Meld[] openMelds, IList<InTurnOperation> operations)
        {
            var alreadyRichied = CurrentRoundStatus.RichiStatus(playerIndex);
            if (alreadyRichied) return;
            var availability = gameSettings.AllowRichiWhenPointsLow || CurrentRoundStatus.GetPoints(playerIndex) >= gameSettings.RichiMortgagePoints;
            if (!availability) return;
            IList<Tile> availableTiles;
            if (MahjongLogic.TestRichi(handTiles, openMelds, justDraw, gameSettings.AllowRichiWhenNotReady, out availableTiles))
            {
                operations.Add(new InTurnOperation
                {
                    Type = InTurnOperationType.Richi,
                    RichiAvailableTiles = availableTiles.ToArray()
                });
            }
        }

        private void TestKongs(int playerIndex, Tile[] handTiles, IList<InTurnOperation> operations)
        {
            if (CurrentRoundStatus.KongClaimed == MahjongConstants.MaxKongs) return; // no more kong can be claimed after 4 kongs claimed
            var alreadyRichied = CurrentRoundStatus.RichiStatus(playerIndex);
            // var handTiles = CurrentRoundStatus.HandTiles(playerIndex);
            if (alreadyRichied)
            {
                // test kongs in richied player hand -- todo
            }
            else
            {
                // 1. test self kongs, aka four same tiles in hand and lastdraw
                var selfKongs = MahjongLogic.GetSelfKongs(handTiles, justDraw);
                if (selfKongs.Any())
                {
                    foreach (var kong in selfKongs)
                    {
                        operations.Add(new InTurnOperation
                        {
                            Type = InTurnOperationType.Kong,
                            Meld = kong
                        });
                    }
                }
                // 2. test add kongs, aka whether a single tile in hand and lastdraw is identical to a pong in open melds
                var addKongs = MahjongLogic.GetAddKongs(CurrentRoundStatus.HandData(playerIndex), justDraw);
                if (addKongs.Any())
                {
                    foreach (var kong in addKongs)
                    {
                        operations.Add(new InTurnOperation
                        {
                            Type = InTurnOperationType.Kong,
                            Meld = kong
                        });
                    }
                }
            }
        }

        private void OnReadinessMessageReceived(NetworkMessage message)
        {
            var content = message.ReadMessage<ClientReadinessMessage>();
            Debug.Log($"[Server] Received ClientReadinessMessage: {content}");
            if (content.Content != MessageIds.ServerDrawTileMessage)
            {
                Debug.LogError("Something is wrong, the received readiness message contains invalid content.");
                return;
            }
            responds[content.PlayerIndex] = true;
        }

        private void OnDiscardTileReceived(NetworkMessage message)
        {
            var content = message.ReadMessage<ClientDiscardRequestMessage>();
            if (content.PlayerIndex != CurrentRoundStatus.CurrentPlayerIndex)
            {
                Debug.Log($"[Server] It is not player {content.PlayerIndex}'s turn to discard a tile, ignoring this message");
                return;
            }
            // handle message
            Debug.Log($"[Server] Received ClientDiscardRequestMessage {content}");
            // Change to discardTileState
            ServerBehaviour.Instance.DiscardTile(
                content.PlayerIndex, content.Tile, content.IsRichiing,
                content.DiscardingLastDraw, content.BonusTurnTime, TurnDoraAfterDiscard);
        }

        private void OnInTurnOperationReceived(NetworkMessage message)
        {
            var content = message.ReadMessage<ClientInTurnOperationMessage>();
            if (content.PlayerIndex != CurrentRoundStatus.CurrentPlayerIndex)
            {
                Debug.Log($"[Server] It is not player {content.PlayerIndex}'s turn to perform a in turn operation, ignoring this message");
                return;
            }
            // handle message according to its type
            Debug.Log($"[Server] Received ClientInTurnOperationMessage: {content}");
            var operation = content.Operation;
            switch (operation.Type)
            {
                case InTurnOperationType.Tsumo:
                    HandleTsumo(operation);
                    break;
                case InTurnOperationType.Bei:
                // todo
                case InTurnOperationType.Kong:
                    HandleKong(operation);
                    break;
                default:
                    Debug.LogError($"[Server] This type of in turn operation should not be sent to server.");
                    break;
            }
        }

        private void OnClientRoundDrawMessageReceived(NetworkMessage message)
        {
            var content = message.ReadMessage<ClientRoundDrawMessage>();
            Debug.Log($"[Server] Received ClientNineOrphansMessage {content}");
            ServerBehaviour.Instance.RoundDraw(content.Type);
        }

        private void HandleTsumo(InTurnOperation operation)
        {
            int playerIndex = CurrentRoundStatus.CurrentPlayerIndex;
            var point = GetTsumoInfo(playerIndex, operation.Tile);
            if (!gameSettings.CheckConstraint(point))
                Debug.LogError(
                    $"Tsumo requires minimum fan of {gameSettings.MinimumFanConstraintType}, but the point only contains {point.FanWithoutDora}");
            ServerBehaviour.Instance.Tsumo(playerIndex, operation.Tile, point);
        }

        private void HandleKong(InTurnOperation operation)
        {
            int playerIndex = CurrentRoundStatus.CurrentPlayerIndex;
            var kong = operation.Meld;
            Debug.Log($"Server is handling the operation of player {playerIndex} of claiming kong {kong}");
            ServerBehaviour.Instance.Kong(playerIndex, kong);
        }

        private PointInfo GetTsumoInfo(int playerIndex, Tile tile)
        {
            var baseHandStatus = HandStatus.Tsumo;
            // test haidi
            if (MahjongSet.TilesRemain == gameSettings.MountainReservedTiles)
                baseHandStatus |= HandStatus.Haidi;
            // test lingshang
            if (IsLingShang) baseHandStatus |= HandStatus.Lingshang;
            var allTiles = MahjongSet.AllTiles;
            var doraTiles = MahjongSet.DoraIndicators.Select(
                indicator => MahjongLogic.GetDoraTile(indicator, allTiles)).ToArray();
            var uraDoraTiles = MahjongSet.UraDoraIndicators.Select(
                indicator => MahjongLogic.GetDoraTile(indicator, allTiles)).ToArray();
            var point = ServerMahjongLogic.GetPointInfo(
                playerIndex, CurrentRoundStatus, tile, baseHandStatus,
                doraTiles, uraDoraTiles, yakuSettings);
            return point;
        }

        public void OnStateUpdate()
        {
            // Debug.Log($"Server is in {GetType().Name}");
            // Sending messages until received all responds from all players
            if (Time.time - lastSendTime > ServerConstants.MessageResendInterval && !responds.All(r => r))
            {
                // resend message
                for (int i = 0; i < players.Count; i++)
                {
                    if (responds[i]) continue;
                    players[i].connectionToClient.Send(
                        i == CurrentPlayerIndex ? MessageIds.ServerDrawTileMessage : MessageIds.ServerOtherDrawTileMessage,
                        messages[i]);
                }
            }
            // Time out
            if (Time.time - firstSendTime > serverTimeOut)
            {
                // force auto discard
                ServerBehaviour.Instance.DiscardTile(CurrentPlayerIndex, (Tile)CurrentRoundStatus.LastDraw, false, true, 0, TurnDoraAfterDiscard);
            }
        }

        public void OnStateExit()
        {
            Debug.Log($"Server exits {GetType().Name}");
            NetworkServer.UnregisterHandler(MessageIds.ClientReadinessMessage);
            NetworkServer.UnregisterHandler(MessageIds.ClientInTurnOperationMessage);
            NetworkServer.UnregisterHandler(MessageIds.ClientDiscardTileMessage);
            NetworkServer.UnregisterHandler(MessageIds.ClientNineOrphansMessage);
            CurrentRoundStatus.CheckOneShot(CurrentPlayerIndex);
        }
    }
}