package com.genesis.roomscan;

import android.app.Activity;
import android.app.Fragment;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.security.MessageDigest;

/** Small Storage Access Framework bridge for one streamed ZIP or GLB. */
public final class MerkabaPackagePicker {
    private static final String FragmentTag = "MerkabaPackagePicker";
    private static final String GameObjectKey = "gameObject";
    private static final String CallbackKey = "callback";
    private static final String GlbModeKey = "glbMode";
    private static final String SaveModeKey = "saveMode";
    private static final String SourcePathKey = "sourcePath";
    private static final String FileNameKey = "fileName";
    private static final String MimeTypeKey = "mimeType";

    private MerkabaPackagePicker() { }

    public static void open(final Activity activity, final String gameObject,
            final String callback) {
        openDocument(activity, gameObject, callback, false);
    }

    public static void openGlb(final Activity activity,
            final String gameObject, final String callback) {
        openDocument(activity, gameObject, callback, true);
    }

    private static void openDocument(final Activity activity,
            final String gameObject, final String callback,
            final boolean glbMode) {
        Bundle arguments = new Bundle();
        arguments.putString(GameObjectKey, gameObject);
        arguments.putString(CallbackKey, callback);
        arguments.putBoolean(GlbModeKey, glbMode);
        showPicker(activity, arguments, gameObject, callback);
    }

    public static void save(final Activity activity, final String sourcePath,
            final String fileName, final String mimeType,
            final String gameObject, final String callback) {
        Bundle arguments = new Bundle();
        arguments.putString(GameObjectKey, gameObject);
        arguments.putString(CallbackKey, callback);
        arguments.putBoolean(SaveModeKey, true);
        arguments.putString(SourcePathKey, sourcePath);
        arguments.putString(FileNameKey, fileName);
        arguments.putString(MimeTypeKey, mimeType);
        showPicker(activity, arguments, gameObject, callback);
    }

    private static void showPicker(final Activity activity,
            final Bundle arguments, final String gameObject,
            final String callback) {
        if (activity == null) {
            send(gameObject, callback, "ERROR:Unity activity is unavailable");
            return;
        }
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                Fragment old = activity.getFragmentManager()
                    .findFragmentByTag(FragmentTag);
                if (old != null) {
                    send(gameObject, callback,
                        "ERROR:A package picker is already open");
                    return;
                }
                PickerFragment fragment = new PickerFragment();
                fragment.setArguments(arguments);
                activity.getFragmentManager().beginTransaction()
                    .add(fragment, FragmentTag).commit();
            }
        });
    }

    public static final class PickerFragment extends Fragment {
        private static final int OpenZipRequest = 0x4d38;
        private boolean launched;
        private FileInputStream saveInput;
        private long saveBytes;

        @Override public void onCreate(Bundle savedInstanceState) {
            super.onCreate(savedInstanceState);
            setRetainInstance(true);
        }

        @Override public void onResume() {
            super.onResume();
            if (launched) return;
            launched = true;
            boolean saveMode = booleanArgument(SaveModeKey);
            if (saveMode) {
                try {
                    openSaveSource();
                } catch (Exception exception) {
                    finish("ERROR:" + exception.getMessage());
                    return;
                }
            }
            Intent intent = new Intent(saveMode ? Intent.ACTION_CREATE_DOCUMENT
                : Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            if (saveMode) {
                intent.setType(argument(MimeTypeKey));
                intent.putExtra(Intent.EXTRA_TITLE, argument(FileNameKey));
                intent.addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION |
                    Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
            } else {
                boolean glbMode = booleanArgument(GlbModeKey);
                intent.setType(glbMode ? "*/*" : "application/zip");
                intent.putExtra(Intent.EXTRA_MIME_TYPES, glbMode
                    ? new String[] { "model/gltf-binary",
                        "application/octet-stream" }
                    : new String[] { "application/zip",
                        "application/x-zip-compressed",
                        "application/octet-stream" });
                intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION |
                    Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
            }
            try {
                startActivityForResult(intent, OpenZipRequest);
            } catch (ActivityNotFoundException exception) {
                finish("ERROR:No Android document picker is installed");
            } catch (RuntimeException exception) {
                finish("ERROR:" + exception.getMessage());
            }
        }

        @Override public void onActivityResult(int requestCode, int resultCode,
                Intent data) {
            super.onActivityResult(requestCode, resultCode, data);
            if (requestCode != OpenZipRequest) return;
            if (resultCode != Activity.RESULT_OK || data == null ||
                    data.getData() == null) {
                finish("CANCELLED");
                return;
            }
            final Activity activity = getActivity();
            final Uri uri = data.getData();
            final String gameObject = argument(GameObjectKey);
            final String callback = argument(CallbackKey);
            if (activity == null) {
                finish("ERROR:Unity activity is unavailable");
                return;
            }
            boolean saveMode = booleanArgument(SaveModeKey);
            takePersistableGrant(activity, uri, data.getFlags(), saveMode
                ? Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                : Intent.FLAG_GRANT_READ_URI_PERMISSION);
            if (saveMode) {
                saveDocument(activity, uri, gameObject, callback);
                return;
            }
            new Thread(new Runnable() {
                @Override public void run() {
                    String result;
                    try {
                        File directory = new File(activity.getFilesDir(),
                            "MerkabaScan/imports");
                        if (!directory.exists() && !directory.mkdirs())
                            throw new IllegalStateException(
                                "Could not create model import directory");
                        boolean glbMode = booleanArgument(GlbModeKey);
                        String extension = glbMode ? ".glb" : ".zip";
                        File temporary = new File(directory,
                            "QuestMerkabaScan-import" + extension + ".tmp");
                        if (temporary.exists() && !temporary.delete())
                            throw new IllegalStateException(
                                "Could not reset temporary import");
                        try (InputStream input = activity.getContentResolver()
                                 .openInputStream(uri);
                             FileOutputStream output =
                                 new FileOutputStream(temporary)) {
                            if (input == null)
                                throw new IllegalStateException(
                                    "Selected document could not be opened");
                            MessageDigest digest = MessageDigest.getInstance(
                                "SHA-256");
                            byte[] buffer = new byte[1024 * 1024];
                            int read;
                            while ((read = input.read(buffer)) >= 0) {
                                if (read == 0) continue;
                                output.write(buffer, 0, read);
                                digest.update(buffer, 0, read);
                            }
                            output.flush();
                            output.getFD().sync();
                            StringBuilder hash = new StringBuilder(64);
                            for (byte value : digest.digest())
                                hash.append(String.format("%02x", value & 0xff));
                            File destination = new File(directory,
                                "QuestMerkabaScan-" + hash + extension);
                            if (destination.exists()) {
                                if (!temporary.delete())
                                    throw new IllegalStateException(
                                        "Could not discard duplicate import");
                            } else if (!temporary.renameTo(destination)) {
                                throw new IllegalStateException(
                                    "Could not publish imported model");
                            }
                            result = destination.getAbsolutePath();
                        }
                    } catch (Exception exception) {
                        result = "ERROR:" + exception.getMessage();
                    }
                    final String delivered = result;
                    activity.runOnUiThread(new Runnable() {
                        @Override public void run() {
                            send(gameObject, callback, delivered);
                            removeSelf();
                        }
                    });
                }
            }, "MerkabaPackageImport").start();
        }

        private void openSaveSource() throws IOException {
            Activity activity = getActivity();
            if (activity == null)
                throw new IOException("Unity activity is unavailable");
            File source = new File(argument(SourcePathKey)).getCanonicalFile();
            boolean allowed = isExportDirectory(source.getParentFile(),
                activity.getFilesDir()) || isExportDirectory(
                    source.getParentFile(), activity.getExternalFilesDir(null));
            String name = argument(FileNameKey);
            String mime = argument(MimeTypeKey);
            boolean format = (source.getName().endsWith(".glb") &&
                "model/gltf-binary".equals(mime)) ||
                (source.getName().endsWith(".zip") && "application/zip".equals(mime));
            if (!allowed || !source.isFile() || !format ||
                    !source.getName().equals(name))
                throw new IOException("Save As requires a completed app export");
            // Pin the completed file before opening the picker. A later atomic
            // export replacement cannot change this descriptor's source bytes.
            saveInput = new FileInputStream(source);
            saveBytes = saveInput.getChannel().size();
            if (saveBytes <= 0L)
                throw new IOException("The completed export is empty");
        }

        private boolean isExportDirectory(File directory, File filesRoot)
                throws IOException {
            return filesRoot != null && directory != null && directory.equals(
                new File(filesRoot, "MerkabaScan/exports").getCanonicalFile());
        }

        private void saveDocument(final Activity activity, final Uri uri,
                final String gameObject, final String callback) {
            final FileInputStream source = saveInput;
            final long expected = saveBytes;
            saveInput = null; // The worker now owns and always closes the stream.
            if (source == null) {
                finish("ERROR:Export source is no longer available; choose Save As again");
                return;
            }
            send(gameObject, callback, "COPYING");
            new Thread(new Runnable() {
                @Override public void run() {
                    String result;
                    try {
                        try (InputStream input = source;
                             OutputStream output = activity.getContentResolver()
                                 .openOutputStream(uri, "wt")) {
                            if (output == null)
                                throw new IOException("Selected document cannot be written");
                            byte[] buffer = new byte[1024 * 1024];
                            long copied = 0L;
                            int read;
                            while ((read = input.read(buffer)) >= 0) {
                                if (read == 0) continue;
                                copied += read;
                                if (copied > expected)
                                    throw new IOException("Export source changed during copy");
                                output.write(buffer, 0, read);
                            }
                            if (copied != expected)
                                throw new IOException("Export source was truncated during copy");
                            output.flush();
                        }
                        result = "SAVED:" + uri.toString();
                    } catch (Exception exception) {
                        result = "ERROR:" + exception.getMessage();
                    }
                    final String delivered = result;
                    activity.runOnUiThread(new Runnable() {
                        @Override public void run() {
                            send(gameObject, callback, delivered);
                            removeSelf();
                        }
                    });
                }
            }, "MerkabaPackageExport").start();
        }

        private void takePersistableGrant(Activity activity, Uri uri,
                int returnedFlags, int requestedMode) {
            int grantedMode = returnedFlags & requestedMode;
            if ((returnedFlags & Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION) == 0 ||
                    grantedMode == 0) return;
            try {
                activity.getContentResolver().takePersistableUriPermission(uri, grantedMode);
            } catch (SecurityException | UnsupportedOperationException exception) {
                // The current one-time permission may still be usable. No
                // persistent access is promised; a later action reopens picker.
                Log.w(FragmentTag, "Document permission is one-time only; " +
                    "select the document again when needed", exception);
            }
        }

        private void closeSaveSource() {
            if (saveInput == null) return;
            try {
                saveInput.close();
            } catch (IOException exception) {
                Log.w(FragmentTag, "Could not close export source", exception);
            }
            saveInput = null;
        }

        @Override public void onDestroy() {
            closeSaveSource();
            super.onDestroy();
        }

        private String argument(String key) {
            Bundle arguments = getArguments();
            return arguments != null ? arguments.getString(key, "") : "";
        }

        private boolean booleanArgument(String key) {
            Bundle arguments = getArguments();
            return arguments != null && arguments.getBoolean(key, false);
        }

        private void finish(String result) {
            closeSaveSource();
            send(argument(GameObjectKey), argument(CallbackKey), result);
            removeSelf();
        }

        private void removeSelf() {
            Activity activity = getActivity();
            if (activity != null && !activity.isFinishing())
                activity.getFragmentManager().beginTransaction()
                    .remove(this).commitAllowingStateLoss();
        }
    }

    private static void send(String gameObject, String callback,
            String value) {
        UnityPlayer.UnitySendMessage(gameObject, callback,
            value != null ? value : "ERROR:Unknown import error");
    }
}
