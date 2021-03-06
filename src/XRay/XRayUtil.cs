﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace XRayBuilderGUI
{
    public static class XRayUtil
    {
        /// <summary>
        /// Extract terms from the given db.
        /// </summary>
        /// <param name="xrayDb">Connection to any db containing the proper dataset.</param>
        /// <param name="singleUse">If set, will close the connection when complete.</param>
        public static IEnumerable<XRay.Term> ExtractTermsNew(DbConnection xrayDb, bool singleUse)
        {
            if (xrayDb.State != ConnectionState.Open)
                xrayDb.Open();
            
            var command = xrayDb.CreateCommand();
            command.CommandText = "SELECT entity.id,entity.label,entity.type,entity.count,entity_description.text,string.text as sourcetxt FROM entity"
                                  + " LEFT JOIN entity_description ON entity.id = entity_description.entity"
                                  + " LEFT JOIN source ON entity_description.source = source.id"
                                  + " LEFT JOIN string ON source.label = string.id AND string.language = 'en'"
                                  + " WHERE entity.has_info_card = '1'";
            var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var newTerm = new XRay.Term
                {
                    Id = Convert.ToInt32(reader["id"]),
                    TermName = (string)reader["label"],
                    Type = Convert.ToInt32(reader["type"]) == 1 ? "character" : "topic",
                    Desc = (string)reader["text"],
                    DescSrc = reader["sourcetxt"] == DBNull.Value ? "" : (string)reader["sourcetxt"]
                };

                // Real locations aren't needed for extracting terms for preview or XML saving, but need count
                var i = Convert.ToInt32(reader["count"]);
                for (; i > 0; i--)
                    newTerm.Locs.Add(null);

                // TODO: Should probably also confirm whether this URL exists or not
                if (newTerm.DescSrc == "Wikipedia")
                    newTerm.DescUrl = string.Format(@"http://en.wikipedia.org/wiki/{0}", newTerm.TermName.Replace(" ", "_"));
                yield return newTerm;
            }

            command.Dispose();
            if (singleUse)
                xrayDb.Close();
        }

        public enum XRayVersion
        {
            Invalid = 0,
            Old,
            New
        }
        
        public static XRayVersion CheckXRayVersion(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var c = fs.ReadByte();
                switch (c)
                {
                    case 'S':
                        return XRayVersion.New;
                    case '{':
                        return XRayVersion.Old;
                    default:
                        return XRayVersion.Invalid;
                }
            }
        }

        public static IEnumerable<XRay.Term> ExtractTermsOld(string path)
        {
            string readContents;
            using (StreamReader streamReader = new StreamReader(path, Encoding.UTF8))
                readContents = streamReader.ReadToEnd();

            var xray = JObject.Parse(readContents);
            foreach (var term in xray["terms"].Children())
                yield return term.ToObject<XRay.Term>();
        }
    }
}
