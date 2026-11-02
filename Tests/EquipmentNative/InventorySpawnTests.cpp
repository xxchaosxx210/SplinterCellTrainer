#include "../../Native/InventorySpawn.h"
#include <cstdio>
struct Vec {float x,y,z;};struct Rot {int pitch,yaw,roll;};
int main(){bool ok=true;int calls=0;auto check=[&](bool b,const char* n){std::printf("%s %s\n",b?"PASS":"FAIL",n);ok&=b;};
 int level=1,itemClass=2,controller=3,item=4;
 auto spawn=[&](void* world,void* cls,unsigned name,Vec at,Rot facing,void* actorTemplate,int noCollisionFail,int remoteOwned,void* owner,void* instigator)->void*{
  ++calls;check(world==&level&&cls==&itemClass,"spawn receives the inventory item's level and class");
  check(actorTemplate==nullptr,"controller is never passed as an actor template");
  check(owner==&controller,"controller is passed in the owner argument after the spawn flags");
  check(noCollisionFail==0&&remoteOwned==0&&instigator==nullptr&&name==0,"normal local inventory spawn flags preserved");
  check(at.x==10&&at.y==20&&at.z==30&&facing.yaw==100,"spawn location and rotation preserved");return &item;
 };
 auto result=InventorySpawn::Create(spawn,&level,&itemClass,Vec{10,20,30},Rot{0,100,0},&controller);
 check(calls==1&&result==&item,"exactly one actor spawn is performed and its result returned");return ok?0:1;}
