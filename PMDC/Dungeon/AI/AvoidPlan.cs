﻿using System;
using System.Collections.Generic;
using RogueElements;
using RogueEssence.Data;
using RogueEssence;
using RogueEssence.Dungeon;

namespace PMDC.Dungeon
{
    [Serializable]
    public abstract class AvoidPlan : AIPlan
    {
        private List<Loc> goalPath;
        private List<Loc> locHistory;

        public AvoidPlan(AIFlags iq, AttackChoice attackPattern) : base(iq, attackPattern)
        {
            goalPath = new List<Loc>();
            locHistory = new List<Loc>();
        }
        protected AvoidPlan(AvoidPlan other) : base(other) { }

        public override void Initialize(Character controlledChar)
        {
            //create a pathfinding map?
        }

        public override void SwitchedIn()
        {
            goalPath = new List<Loc>();
            locHistory = new List<Loc>();
            base.SwitchedIn();
        }

        protected abstract bool RunFromAllies { get; }
        protected abstract bool RunFromFoes { get; }
        protected abstract bool AbortIfCornered { get; }

        public override GameAction Think(Character controlledChar, bool preThink, ReRandom rand)
        {
            if (controlledChar.CantWalk)
                return null;

            Alignment alignment = Alignment.None;
            if (RunFromAllies)
                alignment |= Alignment.Friend;
            if (RunFromFoes)
                alignment |= Alignment.Foe;
            List<Character> seenCharacters = controlledChar.GetSeenCharacters(alignment);

            if (seenCharacters.Count == 0)
                return null;

            CharIndex ownIndex = ZoneManager.Instance.CurrentMap.GetCharIndex(controlledChar);
            if (RunFromAllies && !RunFromFoes)
            {
                //when running from allies and not enemies, run from only the ones that are higher ranking than this one
                CharIndex seenIndex = ZoneManager.Instance.CurrentMap.GetCharIndex(seenCharacters[0]);
                if (seenIndex.Team > ownIndex.Team || seenIndex.Team == ownIndex.Team && seenIndex.Char > ownIndex.Char) //the lowest index is higher than this, therefore there's no one to run away from
                    return null;
            }

            return DumbAvoid(controlledChar, preThink, seenCharacters, ownIndex, rand);
        }

        private GameAction SmartAvoid(Character controlledChar, bool preThink, List<Character> seenCharacters, CharIndex ownIndex, ReRandom rand)
        {
            StablePriorityQueue<double, Dir8> candidateDirs = new StablePriorityQueue<double, Dir8>();

            //use djikstra to find a path out
            //choose the path that avoids other characters the most


            //remove all locs from the locHistory that are no longer on screen
            Loc seen = Character.GetSightDims();
            for (int ii = locHistory.Count - 1; ii >= 0; ii--)
            {
                Loc diff = locHistory[ii] - controlledChar.CharLoc;
                if (Math.Abs(diff.X) > seen.X || Math.Abs(diff.Y) > seen.Y || ii > 15)
                {
                    locHistory.RemoveRange(0, ii);
                    break;
                }
            }
            Loc offset = controlledChar.CharLoc - seen;

            //CHECK FOR ADVANCE
            if (goalPath.Count > 1 && goalPath[goalPath.Count - 2] == controlledChar.CharLoc)//check if we advanced since last time
                goalPath.RemoveAt(goalPath.Count - 1);//remove our previous trail

            //check to see if the end loc is still valid... or, just check to see if *the next step* is still valid
            if (goalPath.Count > 1)
            {
                if (controlledChar.CharLoc == goalPath[goalPath.Count - 1])//check if on the trail
                {
                    if (!ZoneManager.Instance.CurrentMap.TileBlocked(goalPath[goalPath.Count - 2], controlledChar.Mobility) &&
                        !BlockedByTrap(controlledChar, goalPath[goalPath.Count - 2]) &&
                        !BlockedByHazard(controlledChar, goalPath[goalPath.Count - 2]))//check to make sure the next step didn't suddely become blocked
                    {
                        //update current traversals
                        if (locHistory.Count == 0 || locHistory[locHistory.Count - 1] != controlledChar.CharLoc)
                            locHistory.Add(controlledChar.CharLoc);
                        if (!preThink)
                        {
                            Character destChar = ZoneManager.Instance.CurrentMap.GetCharAtLoc(goalPath[goalPath.Count - 2]);
                            if (destChar != null && ZoneManager.Instance.CurrentMap.TerrainBlocked(controlledChar.CharLoc, destChar.Mobility))
                                return new GameAction(GameAction.ActionType.Wait, Dir8.None);
                        }
                        GameAction act = TrySelectWalk(controlledChar, DirExt.GetDir(goalPath[goalPath.Count - 1], goalPath[goalPath.Count - 2]));
                        //attempt to continue the path
                        //however, we can only verify that we continued on the path on the next loop, using the CHECK FOR ADVANCE block
                        return act;
                    }
                }
            }

            //if it isn't find a new end loc
            List<Loc> seenExits = GetAreaExits(controlledChar);

            if (seenExits.Count == 0)
                return null;
            //one element in the acceptable range will be randomly selected to be the one that drives the heuristic

            //later, rate the exits based on how far they are from the tail point of the lochistory
            //add them to a sorted list

            int selectedIndex = -1;
            if (locHistory.Count > 0)
            {
                List<int> forwardFacingIndices = new List<int>();
                Loc pastDir = locHistory[0] - controlledChar.CharLoc;
                for (int ii = 0; ii < seenExits.Count; ii++)
                {
                    if (Loc.Dot(pastDir, (seenExits[ii] - controlledChar.CharLoc)) <= 0)
                        forwardFacingIndices.Add(ii);
                }
                if (forwardFacingIndices.Count > 0)
                    selectedIndex = forwardFacingIndices[rand.Next(forwardFacingIndices.Count)];
            }
            //consolation exit
            if (selectedIndex == -1)
                selectedIndex = rand.Next(seenExits.Count);

            //the selected node will be index 0
            Loc temp = seenExits[0];
            seenExits[0] = seenExits[selectedIndex];
            seenExits[selectedIndex] = temp;

            //if any of the tiles are reached in the search, they will be automatically chosen

            //Analysis:
            //if there is only one exit, and it's easily reached, the speed is the same - #1 fastest case
            //if there are many exits, and they're easily reached, the speed is the same - #1 fastest case
            //if there's one exit, and it's impossible, the speed is the same - #2 fastest case
            //if there's many exits, and they're all impossible, the speed is faster - #2 fastest case
            //if there's many exits, and only the backtrack is possible, the speed is faster - #2 fastest case

            goalPath = GetPathPermissive(controlledChar, seenExits);
            if (locHistory.Count == 0 || locHistory[locHistory.Count - 1] != controlledChar.CharLoc)
                locHistory.Add(controlledChar.CharLoc);

            //TODO: we seldom ever run into other characters who obstruct our path, but if they do, try to wait courteously for them if they are earlier on the team list than us
            //check to make sure we aren't force-warping anyone from their position
            if (!preThink && goalPath.Count > 1)
            {
                Character destChar = ZoneManager.Instance.CurrentMap.GetCharAtLoc(goalPath[goalPath.Count - 2]);
                if (destChar != null && ZoneManager.Instance.CurrentMap.TerrainBlocked(controlledChar.CharLoc, destChar.Mobility))
                    return new GameAction(GameAction.ActionType.Wait, Dir8.None);
            }
            return SelectChoiceFromPath(controlledChar, goalPath, false);

        }
        private GameAction DumbAvoid(Character controlledChar, bool preThink, List<Character> seenCharacters, CharIndex ownIndex, ReRandom rand)
        {
            StablePriorityQueue<double, Dir8> candidateDirs = new StablePriorityQueue<double, Dir8>();

            //choose the single direction that avoids other characters the most
            bool respectPeers = !preThink;

            for (int ii = -1; ii < DirExt.DIR8_COUNT; ii++)
            {
                Loc checkLoc = controlledChar.CharLoc + ((Dir8)ii).GetLoc();

                double dirDistance = 0;
                //iterated in increasing character indices
                foreach (Character seenChar in seenCharacters)
                {
                    if (RunFromAllies && !RunFromFoes)
                    {
                        //only avoid if their character index is lower than this one, aka higher ranking member
                        CharIndex seenIndex = ZoneManager.Instance.CurrentMap.GetCharIndex(seenChar);
                        if (seenIndex.Team > ownIndex.Team)
                            break;
                        else if (seenIndex.Team == ownIndex.Team)
                        {
                            if (seenIndex.Char > ownIndex.Char && seenChar.MemberTeam.LeaderIndex != seenIndex.Char)
                                continue;
                        }
                    }

                    dirDistance += Math.Sqrt((checkLoc - seenChar.CharLoc).DistSquared());
                }

                candidateDirs.Enqueue(-dirDistance, (Dir8)ii);
            }


            Grid.LocTest checkDiagBlock = (Loc testLoc) =>
            {
                return (ZoneManager.Instance.CurrentMap.TileBlocked(testLoc, controlledChar.Mobility, true));
                //enemy/ally blockings don't matter for diagonals
            };

            Grid.LocTest checkBlock = (Loc testLoc) =>
            {
                if (ZoneManager.Instance.CurrentMap.TileBlocked(testLoc, controlledChar.Mobility))
                    return true;

                if ((IQ & AIFlags.TrapAvoider) != AIFlags.None)
                {
                    Tile tile = ZoneManager.Instance.CurrentMap.Tiles[testLoc.X][testLoc.Y];
                    if (tile.Effect.ID > -1)
                    {
                        TileData entry = DataManager.Instance.GetTile(tile.Effect.ID);
                        if (entry.StepType == TileData.TriggerType.Trap || entry.StepType == TileData.TriggerType.Site || entry.StepType == TileData.TriggerType.Switch)
                            return true;
                    }
                }

                if (respectPeers && BlockedByChar(testLoc))
                    return true;

                return false;
            };

            //try each direction from most appealing to least appealing, stopping if we get to "none"
            while (candidateDirs.Count > 0)
            {
                Dir8 highestDir = candidateDirs.Dequeue();
                if (highestDir == Dir8.None)
                {
                    if (AbortIfCornered)//this plan will be aborted, try the next plan in the list
                        return null;
                    else//cry in a corner
                        return new GameAction(GameAction.ActionType.Wait, Dir8.None);
                }
                else
                {
                    //check to see if we can walk this way
                    if (!Grid.IsDirBlocked(controlledChar.CharLoc, highestDir, checkBlock, checkDiagBlock))
                        return TrySelectWalk(controlledChar, highestDir);
                }
            }

            if (AbortIfCornered)//this plan will be aborted, try the next plan in the list
                return null;
            else//cry in a corner
                return new GameAction(GameAction.ActionType.Wait, Dir8.None);
        }
    }
}