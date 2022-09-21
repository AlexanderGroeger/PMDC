﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Reflection;

namespace PMDC.Dev
{
    public sealed class UpgradeBinder : DefaultSerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            Type typeToDeserialize = Type.GetType(String.Format("{0}, {1}",
                typeName, assemblyName));

            if (typeToDeserialize == null)
            {
                typeName = typeName.Replace("RefreshPreEvent", "ElementMobilityEvent");
                if (typeName.StartsWith("RogueEssence.IntrudingBlobWaterStep"))
                {
                    assemblyName = assemblyName.Replace("RogueEssence", "RogueElements");
                    typeName = typeName.Replace("RogueEssence.IntrudingBlobWaterStep", "RogueElements.BlobWaterStep");
                }
                if (typeName.StartsWith("RogueEssence.LevelGen.MobSpawnSettingsStep"))
                {
                    assemblyName = assemblyName.Replace("RogueEssence", "PMDC");
                    typeName = typeName.Replace("RogueEssence.LevelGen.MobSpawnSettingsStep", "PMDC.LevelGen.MobSpawnSettingsStep");
                }
                //typeName = typeName.Replace("From", "To");
                //assemblyName = assemblyName.Replace("From", "To");
                //then the type moved to a new namespace
                typeToDeserialize = Type.GetType(String.Format("{0}, {1}", typeName, assemblyName));
            }
            return typeToDeserialize;
        }
    }
}
