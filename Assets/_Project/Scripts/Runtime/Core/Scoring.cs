using System.Collections.Generic;

namespace MurderMystery.Core
{
    public class GoalResult
    {
        public string GoalId;
        public GoalType Type;
        public int Points;
        public bool Achieved;
    }

    public class ScoreSheet
    {
        public string CharacterId;
        public readonly List<GoalResult> Goals = new List<GoalResult>();
        public int Total;
    }

    /// <summary>
    /// Scoring per game_rules.md §11. accusedId == null means the vote stayed unresolved.
    /// </summary>
    public static class Scoring
    {
        public static Dictionary<string, ScoreSheet> Compute(GameSession session, string accusedId)
        {
            var data = session.Data;
            var culprit = data.Characters.culpritId;
            var answer = data.Characters.bonusQuestion.answerIndex;
            var sheets = new Dictionary<string, ScoreSheet>();

            foreach (var c in data.Characters.characters)
            {
                var p = session.Players[c.id];
                var sheet = new ScoreSheet { CharacterId = c.id };
                foreach (var g in c.goals)
                {
                    var achieved = g.Type switch
                    {
                        GoalType.VoteCulprit => accusedId == culprit,
                        GoalType.VoteNotAccused => accusedId != c.id,
                        GoalType.Possess => System.Array.TrueForAll(g.itemIds, p.Inventory.Contains),
                        GoalType.Secret => !session.IsExposed(g.id),
                        GoalType.BonusIntent => !c.isCulprit && p.BonusAnswer == answer,
                        _ => false
                    };
                    sheet.Goals.Add(new GoalResult { GoalId = g.id, Type = g.Type, Points = g.points, Achieved = achieved });
                    if (achieved) sheet.Total += g.points;
                }
                sheets[c.id] = sheet;
            }
            return sheets;
        }
    }
}
