package com.dolphindotnet.rider;

import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.components.*;
import org.jetbrains.annotations.NotNull;
import java.util.*;

@Service(Service.Level.APP)
@State(name = "DolphinDotNetTargets", storages = @Storage(value = "dolphindotnet-targets.xml", roamingType = RoamingType.DISABLED))
public final class TargetStore implements PersistentStateComponent<TargetStore.Data> {
    public static final class Data { public List<TargetProfile> targets = new ArrayList<>(); }
    private Data data = new Data();
    public static TargetStore getInstance() { return ApplicationManager.getApplication().getService(TargetStore.class); }
    @Override public synchronized @NotNull Data getState() {
        var snapshot = new Data(); snapshot.targets = copies(data.targets); return snapshot;
    }
    @Override public synchronized void loadState(@NotNull Data state) { data = new Data(); data.targets = copies(state.targets); }
    public synchronized List<TargetProfile> targets() { return copies(data.targets); }
    public synchronized TargetProfile find(String id) {
        return data.targets.stream().filter(t -> Objects.equals(t.id, id)).findFirst().map(TargetProfile::copy).orElse(null);
    }
    public synchronized void replace(List<TargetProfile> targets) {
        var ids = new HashSet<String>();
        for (var t : targets) {
            t.validate();
            if (!ids.add(t.id)) throw new IllegalArgumentException("Duplicate target identity.");
        }
        data.targets = copies(targets);
    }
    private static List<TargetProfile> copies(List<TargetProfile> targets) { return targets.stream().map(TargetProfile::copy).collect(java.util.stream.Collectors.toCollection(ArrayList::new)); }
}
