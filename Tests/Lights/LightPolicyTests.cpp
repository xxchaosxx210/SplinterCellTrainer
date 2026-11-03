#include "../../Native/LightPolicy.h"
#include <cstdio>
static bool Check(bool condition,const char* label){if(!condition){std::fprintf(stderr,"FAIL %s\n",label);return false;}std::printf("PASS %s\n",label);return true;}
int main(){bool ok=true;
 ok&=Check(LightPolicy::ShouldSuppress(true,true,false,false),"supported current-level light is included");
 ok&=Check(!LightPolicy::ShouldSuppress(false,true,false,false),"non-light actors are excluded");
 ok&=Check(!LightPolicy::ShouldSuppress(true,false,false,false),"other levels and class defaults are excluded");
 ok&=Check(!LightPolicy::ShouldSuppress(true,true,true,false),"player and inventory-owned lighting is excluded");
 ok&=Check(!LightPolicy::ShouldSuppress(true,true,false,true),"deleted actors are excluded");
 ok&=Check(LightPolicy::ShouldRestore(true,false),"surviving original light can be restored");
 ok&=Check(!LightPolicy::ShouldRestore(false,false)&&!LightPolicy::ShouldRestore(true,true),"reused identities and destroyed fixtures are not restored");
 ok&=Check(LightPolicy::NeedsRestore(0,255),"a muted light restores its captured brightness");
 ok&=Check(LightPolicy::NeedsRestore(1,128),"engine-modified values restore to the original snapshot on toggle off");
 ok&=Check(LightPolicy::NeedsRestore(1,0)&&!LightPolicy::NeedsRestore(0,0),"lights that were originally off remain off");
 return ok?0:1;}
