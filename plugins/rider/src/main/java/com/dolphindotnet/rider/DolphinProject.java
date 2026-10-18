package com.dolphindotnet.rider;

import com.intellij.openapi.project.Project;
import com.intellij.openapi.vfs.VirtualFile;
import com.intellij.openapi.vfs.VfsUtilCore;
import com.intellij.psi.util.CachedValueProvider;
import com.intellij.psi.util.CachedValuesManager;
import javax.xml.stream.XMLInputFactory;
import javax.xml.stream.XMLStreamConstants;
import java.io.StringReader;

final class DolphinProject {
    private DolphinProject() { }
    static boolean contains(VirtualFile file, Project project) {
        for (var directory = file.getParent(); directory != null; directory = directory.getParent()) {
            boolean projectDirectory = false;
            for (var child : directory.getChildren()) {
                if (!child.isDirectory() && "csproj".equalsIgnoreCase(child.getExtension())) {
                    projectDirectory = true;
                    boolean usesSdk = CachedValuesManager.getManager(project).getCachedValue(child, () -> {
                        boolean found = false;
                        try { found = usesSdk(VfsUtilCore.loadText(child)); } catch (Exception ignored) { }
                        return CachedValueProvider.Result.create(found, child);
                    });
                    if (usesSdk) return true;
                }
            }
            if (projectDirectory || directory.getPath().equals(project.getBasePath())) break;
        }
        return false;
    }
    static boolean usesSdk(String xml) {
        try {
            var factory = XMLInputFactory.newFactory();
            factory.setProperty(XMLInputFactory.SUPPORT_DTD, false);
            factory.setProperty(XMLInputFactory.IS_SUPPORTING_EXTERNAL_ENTITIES, false);
            var reader = factory.createXMLStreamReader(new StringReader(xml));
            try {
                while (reader.hasNext()) {
                    if (reader.next() != XMLStreamConstants.START_ELEMENT) continue;
                    var element = reader.getLocalName();
                    var sdk = reader.getAttributeValue(null, element.equals("Sdk") ? "Name" : "Sdk");
                    if ((element.equals("Project") || element.equals("Sdk")) && sdk != null) {
                        for (var item : sdk.split(";")) {
                            var name = item.trim();
                            if (name.equals("DolphinDotNet.Sdk") || name.startsWith("DolphinDotNet.Sdk/")) return true;
                        }
                    }
                    if (element.equals("Import")) {
                        var path = reader.getAttributeValue(null, "Project");
                        if (path != null) {
                            path = "/" + path.replace('\\', '/');
                            if (path.contains("/DolphinDotNet.Sdk/Sdk/")) return true;
                        }
                    }
                }
            } finally { reader.close(); }
        } catch (Exception ignored) { }
        return false;
    }
}
