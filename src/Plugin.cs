using BepInEx;
using System.Security.Permissions;
using static CreatureTemplate.Relationship.Type;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace Fof;

[BepInPlugin("com.dual.fof", "FoF", "1.2.1")]
sealed class Plugin : BaseUnityPlugin
{
    private static Tracker.CreatureRepresentation MutualFriend(AbstractCreature one, AbstractCreature two)
    {
        if (two.state.dead || one.abstractAI?.RealAI?.tracker is not Tracker tracker || two?.abstractAI?.RealAI?.tracker is not Tracker) {
            return null;
        }

        foreach (Tracker.CreatureRepresentation friendRep in tracker.creatures) {
            AbstractCreature friend = friendRep.representedCreature;

            // If `self` is friends with `friend`, and `other` is friends with `friend`, then it's mutual!
            if (friend != two && friend.state.alive && Friends(one, friend) && Friends(two, friend)) {
                return friendRep;
            }
        }

        return null;
    }

    private static bool Friends(AbstractCreature self, AbstractCreature other)
    {
        if (self?.state == null || other?.state == null || self.state.dead || other.state.dead) {
            return false;
        }
        // All slugcats are friends. Peace and love.
        if (self.realizedObject is Player && other.realizedObject is Player) {
            return true;
        }
        // The target of a friend tracker is obviously a friend.
        if (self.abstractAI?.RealAI?.friendTracker is FriendTracker f && f.friendRel?.subjectID == other.ID) {
            return true;
        }
        // Pack members are friends.
        var otherRep = self.abstractAI?.RealAI?.tracker?.RepresentationForCreature(other, false);
        if (otherRep?.dynamicRelationship?.currentRelationship.type == Pack) {
            return true;
        }

        float rep = RepOfCreature(self, other);

        // If we like the player then chill.
        if (rep > 0.5f) {
            return true;
        }

        // If we hate the player then do NOT chill!
        if (rep < -0.5f) {
            return false;
        }

        // If we're neutral to the player, but other creatures like them, then chill.
        if (self.creatureTemplate?.communityID != null && self.world.game.session is StoryGameSession sess && sess.creatureCommunities is CreatureCommunities communities
            && other.realizedCreature is Player p
            && communities.LikeOfPlayer(self.creatureTemplate.communityID, self.world.region?.regionNumber ?? -1, p.playerState.playerNumber) > 0.8f) {
            return rep > -0.1f;
        }

        return false;
    }

    private static float RepOfCreature(AbstractCreature self, AbstractCreature other)
    {
        var otherRep = self.abstractAI?.RealAI?.tracker?.RepresentationForCreature(other, false);

        float rep = 0;
        if (otherRep != null && other.realizedCreature is Player) {
            if (self.realizedObject is Scavenger scav) rep = scav.AI.LikeOfPlayer(otherRep.dynamicRelationship);
            if (self.realizedObject is Lizard liz) rep = liz.AI.LikeOfPlayer(otherRep);
            if (self.realizedObject is Cicada cicada) rep = cicada.AI.LikeOfPlayer(otherRep);
        }
        else if (self.state.socialMemory?.GetRelationship(other.ID)?.like is float like) {
            rep = like;
        }

        return rep;
    }

    private static CreatureTemplate.Relationship? FriendOfFriendRelationship(AbstractCreature one, AbstractCreature two)
    {
        if (MutualFriend(one, two) is Tracker.CreatureRepresentation mutualFriend) {
            // Pack members of friends are pack members of ours.
            if (mutualFriend.dynamicRelationship.currentRelationship.type == Pack) {
                return new CreatureTemplate.Relationship(Pack, mutualFriend.dynamicRelationship.currentRelationship.intensity * 0.5f);
            }

            // Politely ignore them otherwise.
            return new CreatureTemplate.Relationship(Ignores, 0.5f);
        }

        // Check friends
        if (one.abstractAI?.RealAI?.friendTracker?.friend != null && two.abstractAI?.RealAI?.friendTracker?.friend != null &&
            one.abstractAI.RealAI.friendTracker.friend.abstractCreature.ID == two.abstractAI.RealAI.friendTracker.friend.abstractCreature.ID) {
            return new(one.abstractAI.RealAI.friendTracker.friendRel.like > 0.8f ? Pack : Ignores, 0.5f);
        }

        // Check social memory
        if (one.state.socialMemory != null && two.state.socialMemory != null) {
            foreach (var rel in one.state.socialMemory.relationShips) {
                if (rel.like > 0.8f && two.state.socialMemory.GetLike(rel.subjectID) > 0.8f) {
                    return new(Ignores, 0.5f);
                }
            }
        }

        // Check communities
        if (one.world.game.GetStorySession?.creatureCommunities is CreatureCommunities communities && one.world.game.Players.Count > 0
            && communities.LikeOfPlayer(one.creatureTemplate.communityID, one.world.region?.regionNumber ?? -1, 0) > 0.8f
            && communities.LikeOfPlayer(two.creatureTemplate.communityID, two.world.region?.regionNumber ?? -1, 0) > 0.8f
            && RepOfCreature(one, two) > -0.1f
            && RepOfCreature(one, one.world.game.Players[0]) > -0.1f
            && RepOfCreature(two, one.world.game.Players[0]) > -0.1f
            ) {
            return new(Ignores, 0.5f);
        }

        return null;
    }

    private static bool FriendOfFriend(AbstractCreature one, AbstractCreature two)
    {
        return FriendOfFriendRelationship(one, two) != null;
    }

    public void OnEnable()
    {
        On.RelationshipTracker.DynamicRelationship.Update += DynamicRelationship_Update;
        On.LizardAI.DoIWantToBiteThisCreature += LizardAI_DoIWantToBiteThisCreature;
    }

    private void DynamicRelationship_Update(On.RelationshipTracker.DynamicRelationship.orig_Update orig, RelationshipTracker.DynamicRelationship self)
    {
        if (FriendOfFriendRelationship(self.rt.AI.creature, self.trackerRep.representedCreature) is CreatureTemplate.Relationship fof) {
            (self.rt.AI as IUseARelationshipTracker).UpdateDynamicRelationship(self);
            if (fof.type != self.currentRelationship.type) {
                self.rt.SortCreatureIntoModule(self, fof);
            }
            self.trackerRep.priority = fof.intensity * self.trackedByModuleWeigth;
            self.currentRelationship = fof;
        }
        else {
            orig(self);
        }
    }

    private bool LizardAI_DoIWantToBiteThisCreature(On.LizardAI.orig_DoIWantToBiteThisCreature orig, LizardAI self, Tracker.CreatureRepresentation otherCrit)
    {
        if (!orig(self, otherCrit)) {
            return false;
        }
        // If we have a mutual friend, don't fight.
        if (FriendOfFriend(self.creature, otherCrit.representedCreature)) {
            return false;
        }
        return true;
    }
}
