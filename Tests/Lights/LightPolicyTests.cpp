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
 ok&=Check(LightPolicy::StillMuted(0)&&!LightPolicy::StillMuted(1)&&!LightPolicy::StillMuted(128),"restoration does not overwrite a newer nonzero game value");
 return ok?0:1;}
