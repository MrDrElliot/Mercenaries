#pragma once
#include "Core/Module/ModuleInterface.h"

namespace Lumina
{
    class MERCENARIES_API FMercenariesModule : public IModuleInterface
    {
    public:
        
        void StartupModule() override;
        void ShutdownModule() override;
        
    };
}
