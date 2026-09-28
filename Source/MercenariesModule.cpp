
#include "MercenariesModule.h"
#include "Core/Module/ModuleManager.h"
#include "Log/Log.h"

using namespace Lumina;

IMPLEMENT_MODULE(FMercenariesModule, "Mercenaries");

void FMercenariesModule::StartupModule()
{
    LOG_INFO("Mercenaries Startup Module");
}

void FMercenariesModule::ShutdownModule()
{
    LOG_INFO("Mercenaries Shutdown Module");

}
