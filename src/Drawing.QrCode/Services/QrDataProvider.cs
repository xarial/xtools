﻿//*********************************************************************
//CAD+ Toolset
//Copyright(C) 2022 Xarial Pty Limited
//Product URL: https://cadplus.xarial.com
//License: https://cadplus.xarial.com/license/
//*********************************************************************

using EPDM.Interop.epdm;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xarial.CadPlus.Drawing.QrCode.Data;
using Xarial.CadPlus.Plus.Exceptions;
using Xarial.CadPlus.Plus.Services;
using Xarial.XCad;
using Xarial.XCad.Base;
using Xarial.XCad.Base.Enums;
using Xarial.XCad.Data;
using Xarial.XCad.Documents;
using Xarial.XCad.Documents.Enums;

namespace Xarial.CadPlus.Drawing.QrCode.Services
{
    public class QrDataProvider
    {
        private class DataSourceDocument : IDisposable
        {
            internal IXDocument Document { get; }
            internal IXConfiguration Configuration { get; }

            private readonly IXDrawing m_Drawing;

            private readonly IXLogger m_Logger;

            private readonly IDocumentMetadataAccessLayer m_DrwMal;
            private readonly IDocumentMetadataAccessLayer m_RefDocMal;

            internal DataSourceDocument(IDocumentMetadataAccessLayerProvider docMalProvider, IXDrawing drw, IXLogger logger, bool useRefDoc)
            {
                m_Logger = logger;

                m_DrwMal = docMalProvider.Create(drw, true);

                m_Logger.Log($"Data source drawing is fallback: {m_DrwMal.IsFallbackDocument}", LoggerMessageSeverity_e.Debug);

                m_Drawing = (IXDrawing)m_DrwMal.Document;

                if (useRefDoc)
                {
                    var view = m_Drawing.Sheets.Active.DrawingViews.FirstOrDefault();

                    if (view == null)
                    {
                        throw new UserException("No drawing views in this document");
                    }

                    var refDoc = view.ReferencedDocument;

                    if (refDoc == null)
                    {
                        throw new UserException("View does not have a document");
                    }

                    var conf = view.ReferencedConfiguration;

                    m_RefDocMal = docMalProvider.Create(refDoc, true);

                    m_Logger.Log($"Data source reference document is fallback: {m_RefDocMal.IsFallbackDocument}", LoggerMessageSeverity_e.Debug);

                    refDoc = (IXDocument3D)m_RefDocMal.Document;

                    if (m_RefDocMal.IsFallbackDocument)
                    {
                        conf = refDoc.Configurations[conf.Name];
                    }

                    Document = refDoc;
                    Configuration = conf;
                }
                else
                {
                    Document = m_Drawing;
                }
            }

            public void Dispose()
            {
                m_DrwMal?.Dispose();
                m_RefDocMal?.Dispose();
            }
        }

        private readonly IXApplication m_App;

        private readonly IDocumentMetadataAccessLayerProvider m_DocMalProvider;

        private readonly IXLogger m_Logger;

        public QrDataProvider(IXApplication app, IXLogger logger, IDocumentMetadataAccessLayerProvider docMalProvider)
        {
            m_App = app;
            m_DocMalProvider = docMalProvider;
            m_Logger = logger;
        }

        public string GetData(IXDrawing drw, SourceData srcData)
        {
            var src = srcData.Source;

            using (var scopedDoc = new DataSourceDocument(m_DocMalProvider, drw, m_Logger, srcData.ReferencedDocument))
            {
                IEdmVault5 vault;

                var doc = scopedDoc.Document;
                var conf = scopedDoc.Configuration;

                switch (src)
                {
                    case Source_e.FilePath:
                        return doc.Path;

                    case Source_e.PartNumber:
                        if (conf == null)
                        {
                            throw new UserException("Part number can only be extracted from the configuration of part or assembly");
                        }
                        return conf.PartNumber;

                    case Source_e.CustomProperty:
                        IXProperty prp = null;

                        if (conf != null)
                        {
                            conf.Properties.TryGet(srcData.CustomPropertyName, out prp);
                        }

                        if (prp == null)
                        {
                            doc.Properties.TryGet(srcData.CustomPropertyName, out prp);
                        }

                        if (prp != null)
                        {
                            return prp.Value?.ToString();
                        }
                        else
                        {
                            throw new UserException("Specified custom property does not exist");
                        }

                    case Source_e.PdmVaultLink:
                        const string CONISIO_URL_ACTION = "explore";
                        FindRelativeVaultPath(doc.Path, out vault);
                        var file = vault.GetFileFromPath(doc.Path, out IEdmFolder5 folder);
                        return $"conisio://{vault.Name}/{CONISIO_URL_ACTION}?projectid={folder.ID}&documentid={file.ID}&objecttype={(int)file.ObjectType}";

                    case Source_e.PdmWeb2Url:
                        if (string.IsNullOrEmpty(srcData.PdmWeb2Server))
                        {
                            throw new UserException("Url of Web2 server is not specified");
                        }

                        var vaultRelPath = FindRelativeVaultPath(doc.Path, out vault);
                        return $"{srcData.PdmWeb2Server}/{vault.Name}/{Path.GetDirectoryName(vaultRelPath).Replace('\\', '/')}?view=bom&file={Path.GetFileName(vaultRelPath)}";

                    case Source_e.Custom:
                        return srcData.CustomValue;

                    default:
                        throw new NotSupportedException();
                }
            }
        }

        private string FindRelativeVaultPath(string filePath, out IEdmVault5 vault)
        {
            vault = new EdmVault5Class();

            var vaultName = vault.GetVaultNameFromPath(filePath);

            if (string.IsNullOrEmpty(vaultName))
            {
                throw new UserException("This file is not a part of any vault");
            }

            vault.LoginAuto(vaultName, m_App.WindowHandle.ToInt32());

            if (vault.IsLoggedIn)
            {
                var rootFolderPath = vault.RootFolderPath;

                return filePath.Substring(rootFolderPath.Length + 1, filePath.Length - rootFolderPath.Length - 1);
            }
            else
            {
                throw new UserException($"Failed to login to '{vaultName}' vault");
            }
        }
    }
}
