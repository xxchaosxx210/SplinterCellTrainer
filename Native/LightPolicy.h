#pragma once
namespace LightPolicy {
inline bool ShouldSuppress(bool supportedLight,bool sameLevel,bool ownedByInventoryOrPlayer,bool deleted){return supportedLight&&sameLevel&&!ownedByInventoryOrPlayer&&!deleted;}
inline bool ShouldRestore(bool sameIdentity,bool destroyed){return sameIdentity&&!destroyed;}
inline bool NeedsRestore(unsigned current,unsigned original){return current!=original;}
}
