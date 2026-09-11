package com.genesis.roomscan;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.net.Uri;
import java.io.File;

// Runs before UnityPlayer creates Vulkan. No public provider API or storage
// permission. Manufacturing output uses the app-specific external directory
// so adb can retrieve it without a debuggable build or a different PSO config.
public final class MerkabaPipelineBootstrap extends ContentProvider {
    private static native int configure(String directory);
    private static native boolean captureEnabled();

    @Override public boolean onCreate() {
        System.loadLibrary("MerkabaVulkanTimestamps");
        File root = captureEnabled() ? getContext().getExternalFilesDir(null) : getContext().getFilesDir();
        if (root == null) throw new IllegalStateException("Merkaba pipeline capture storage unavailable");
        File directory = new File(root, "MerkabaPipelineCapture");
        if ((!directory.isDirectory() && !directory.mkdirs()) ||
                configure(directory.getAbsolutePath()) != 0)
            throw new IllegalStateException("Merkaba pipeline binary bootstrap failed");
        return true;
    }
    @Override public Cursor query(Uri u, String[] p, String s, String[] a, String o) {
        throw new UnsupportedOperationException();
    }
    @Override public String getType(Uri u) { return null; }
    @Override public Uri insert(Uri u, ContentValues v) { throw new UnsupportedOperationException(); }
    @Override public int delete(Uri u, String s, String[] a) { throw new UnsupportedOperationException(); }
    @Override public int update(Uri u, ContentValues v, String s, String[] a) {
        throw new UnsupportedOperationException();
    }
}
