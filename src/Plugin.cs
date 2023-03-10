using BepInEx;
using System;
using System.Security.Permissions;
using static CreatureTemplate.Relationship.Type;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace Fof;

[BepInPlugin("com.dual.fof", "FoF", "1.0.2")]
sealed class Plugin : BaseUnityPlugin
{
    private static Tracker.CreatureRepresentation MutualFriend(AbstractCreature self, AbstractCreature other)
    {
        if (other.state.dead || self.abstractAI?.RealAI?.tracker is not Tracker tracker || other?.abstractAI?.RealAI?.tracker is not Tracker fofTracker) {
            return null;
        }

        foreach (Tracker.CreatureRepresentation friendRep in tracker.creatures) {
            AbstractCreature friend = friendRep.representedCreature;

            // If `self` is friends with `friend`, and `other` is friends with `friend`, then it's mutual!
            if (friend != other && friend.state.alive
                && Friends(self, friendRep)
                && Friends(other, fofTracker.RepresentationForCreature(friend, addIfMissing: false))
                ) {
                return friendRep;
            }
        }
        return null;
    }

    private static bool Friends(AbstractCreature self, Tracker.CreatureRepresentation other)
    {
        if (other == null) {
            return false;
        }
        // All slugcats are friends. Peace and love.
        if (self.realizedObject is Player && other.representedCreature.realizedObject is Player) {
            return true;
        }
        // The target of a friend tracker is obviously a friend.
        if (self.abstractAI.RealAI.friendTracker is FriendTracker f && f.friendRel?.subjectID == other.representedCreature.ID) {
            return true;
        }
        // Pack members are friends.
        if (other.dynamicRelationship.currentRelationship.type == Pack) {
            return true;
        }

        float rep = 0;
        if (other.representedCreature.realizedCreature is Player) {
            if (self.realizedObject is Scavenger scav) rep = scav.AI.LikeOfPlayer(other.dynamicRelationship);
            if (self.realizedObject is Lizard liz) rep = liz.AI.LikeOfPlayer(other);
            if (self.realizedObject is Cicada cicada) rep = cicada.AI.LikeOfPlayer(other);
        }

        // If we like the player then chill.
        if (rep > 0.5f) {
            return true;
        }

        // If we're neutral to the player, but other creatures like them, then chill.
        if (self.world.game.GetStorySession?.creatureCommunities is CreatureCommunities communities 
            && other.representedCreature.realizedCreature is Player p
            && communities.LikeOfPlayer(self.creatureTemplate.communityID, self.world.region?.regionNumber ?? -1, p.playerState.playerNumber) > 0.8f) {
            return rep >= 0f;
        }

        return false;
    }

    private CreatureTemplate.Relationship? FriendOfFriendRelationship(RelationshipTracker.DynamicRelationship rel)
    {
        // If already fine with the target, stay that way.
        if (rel.currentRelationship.type == Ignores || rel.currentRelationship.type == Pack || rel.currentRelationship.type == Uncomfortable) {
            return null;
        }

        AbstractCreature self = rel.rt.AI.creature;
        AbstractCreature fof = rel.trackerRep.representedCreature;

        if (MutualFriend(self, fof) is Tracker.CreatureRepresentation mutualFriend) {
            // Pack members of friends are pack members of ours.
            if (mutualFriend.dynamicRelationship.currentRelationship.type == Pack) {
                return new CreatureTemplate.Relationship(Pack, mutualFriend.dynamicRelationship.currentRelationship.intensity * 0.5f);
            }

            // Politely ignore them otherwise.
            return new CreatureTemplate.Relationship(Ignores, 0.5f);
        }

        return null;
    }

    public void OnEnable()
    {
        On.RelationshipTracker.DynamicRelationship.Update += DynamicRelationship_Update;
        On.LizardAI.DoIWantToBiteThisCreature += LizardAI_DoIWantToBiteThisCreature;
    }

    private void DynamicRelationship_Update(On.RelationshipTracker.DynamicRelationship.orig_Update orig, RelationshipTracker.DynamicRelationship self)
    {
        if (FriendOfFriendRelationship(self) is CreatureTemplate.Relationship fof) {
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
        if (MutualFriend(self.creature, otherCrit.representedCreature) != null) {
            return false;
        }
        return true;
    }
}
