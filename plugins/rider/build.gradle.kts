plugins {
    java
    id("org.jetbrains.intellij.platform") version "2.19.0"
}

version = "0.2.0"
group = "com.dolphindotnet"
repositories {
    mavenCentral()
    intellijPlatform { defaultRepositories() }
}
dependencies {
    intellijPlatform {
        // Pin the SDK. Interactive Rider acceptance is a separate release gate.
        if (providers.gradleProperty("riderSdk").isPresent) local(providers.gradleProperty("riderSdk").get())
        else rider("2026.2.3.1")
        bundledModule("intellij.platform.debugger")
        pluginVerifier()
    }
    implementation("org.eclipse.lsp4j:org.eclipse.lsp4j.debug:0.24.0")
    testImplementation("org.junit.jupiter:junit-jupiter:5.11.4")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}
java { toolchain { languageVersion = JavaLanguageVersion.of(25) } }
intellijPlatform {
    pluginConfiguration {
        name = "DolphinDotNet"
        ideaVersion { sinceBuild = "262"; untilBuild = "262.*" }
    }
    pluginVerification { ides { current() } }
}
val adapterOutput = layout.buildDirectory.dir("adapter")
val publishAdapter by tasks.registering(Exec::class) {
    workingDir = rootDir.resolve("../..")
    commandLine("dotnet", "publish", "tools/DolphinDotNet.DebugAdapter", "-c", "Release", "--no-self-contained", "-o", adapterOutput.get().asFile.absolutePath)
    inputs.files(fileTree("../../tools/DolphinDotNet.DebugAdapter") { exclude("bin/**", "obj/**") },
        fileTree("../../tools/DolphinDotNet.Debugger") { exclude("bin/**", "obj/**") })
    outputs.dir(adapterOutput)
}
tasks.prepareSandbox {
    dependsOn(publishAdapter)
    from(adapterOutput) { into("${pluginName.get()}/adapter") }
}

tasks.test {
    useJUnitPlatform()
    dependsOn(publishAdapter)
    systemProperty("dnd.adapter", adapterOutput.get().file("DolphinDotNet.DebugAdapter.dll").asFile.absolutePath)
    systemProperty("dnd.repositoryRoot", rootDir.resolve("../..").canonicalPath)
}
