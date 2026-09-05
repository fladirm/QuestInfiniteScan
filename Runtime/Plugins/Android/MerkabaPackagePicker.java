package com.genesis.roomscan;

import android.app.Activity;
import android.app.Fragment;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.os.ParcelFileDescriptor;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.security.MessageDigest;

/** Small Storage Access Framework bridge for one streamed ZIP or GLB. */
public final class MerkabaPackagePicker {
    private static final String FragmentTag = "MerkabaPackagePicker";
    private static final String GameObjectKey = "gameObject";
    private static final String CallbackKey = "callback";
    private static final String GlbModeKey = "glbMode";
    private static final String SaveModeKey = "saveMode";
    private static final String SourcePathKey = "sourcePath";
    private static final String SuggestedNameKey = "suggestedName";
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

    public static void save(final Activity activity, final String sourcePath,
            final String suggestedName, final String mimeType,
            final String gameObject, final String callback) {
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
                        "ERROR:A document picker is already open");
                    return;
                }
                PickerFragment fragment = new PickerFragment();
                Bundle arguments = new Bundle();
                arguments.putString(GameObjectKey, gameObject);
                arguments.putString(CallbackKey, callback);
                arguments.putBoolean(SaveModeKey, true);
                arguments.putString(SourcePathKey, sourcePath);
                arguments.putString(SuggestedNameKey, suggestedName);
                arguments.putString(MimeTypeKey, mimeType);
                fragment.setArguments(arguments);
                activity.getFragmentManager().beginTransaction()
                    .add(fragment, FragmentTag).commit();
            }
        });
    }

    private static void openDocument(final Activity activity,
            final String gameObject, final String callback,
            final boolean glbMode) {
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
                Bundle arguments = new Bundle();
                arguments.putString(GameObjectKey, gameObject);
                arguments.putString(CallbackKey, callback);
                arguments.putBoolean(GlbModeKey, glbMode);
                fragment.setArguments(arguments);
                activity.getFragmentManager().beginTransaction()
                    .add(fragment, FragmentTag).commit();
            }
        });
    }

    public static final class PickerFragment extends Fragment {
        private static final int OpenZipRequest = 0x4d38;
        private boolean launched;

        @Override public void onCreate(Bundle savedInstanceState) {
            super.onCreate(savedInstanceState);
            setRetainInstance(true);
        }

        @Override public void onResume() {
            super.onResume();
            if (launched) return;
            launched = true;
            boolean saveMode = booleanArgument(SaveModeKey);
            Intent intent = new Intent(saveMode ? Intent.ACTION_CREATE_DOCUMENT
                : Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            boolean glbMode = booleanArgument(GlbModeKey);
            if (saveMode) {
                intent.setType(argument(MimeTypeKey));
                intent.putExtra(Intent.EXTRA_TITLE,
                    argument(SuggestedNameKey));
                intent.addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            } else {
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
            final boolean saveMode = booleanArgument(SaveModeKey);
            final String sourcePath = argument(SourcePathKey);
            new Thread(new Runnable() {
                @Override public void run() {
                    String result;
                    try {
                        result = saveMode
                            ? saveDocument(activity, uri, sourcePath)
                            : importDocument(activity, uri,
                                booleanArgument(GlbModeKey));
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

        private static String saveDocument(Activity activity, Uri uri,
                String sourcePath) throws Exception {
            File source = new File(sourcePath);
            if (!source.isFile())
                throw new IllegalStateException(
                    "Completed export is unavailable");
            try (FileInputStream input = new FileInputStream(source);
                 ParcelFileDescriptor descriptor = activity
                     .getContentResolver().openFileDescriptor(uri, "w")) {
                if (descriptor == null)
                    throw new IllegalStateException(
                        "Selected destination could not be opened");
                try (FileOutputStream output = new FileOutputStream(
                         descriptor.getFileDescriptor())) {
                    copy(input, output, null);
                    output.flush();
                    output.getFD().sync();
                }
            }
            return "SAVED:" + uri.toString();
        }

        private static String importDocument(Activity activity, Uri uri,
                boolean glbMode) throws Exception {
            File directory = new File(activity.getFilesDir(),
                "MerkabaScan/imports");
            if (!directory.exists() && !directory.mkdirs())
                throw new IllegalStateException(
                    "Could not create model import directory");
            String extension = glbMode ? ".glb" : ".zip";
            File temporary = new File(directory,
                "QuestMerkabaScan-import" + extension + ".tmp");
            if (temporary.exists() && !temporary.delete())
                throw new IllegalStateException(
                    "Could not reset temporary import");
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            try (InputStream input = activity.getContentResolver()
                     .openInputStream(uri);
                 FileOutputStream output = new FileOutputStream(temporary)) {
                if (input == null)
                    throw new IllegalStateException(
                        "Selected document could not be opened");
                copy(input, output, digest);
                output.flush();
                output.getFD().sync();
            }
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
            return destination.getAbsolutePath();
        }

        private static void copy(InputStream input, FileOutputStream output,
                MessageDigest digest) throws Exception {
            byte[] buffer = new byte[1024 * 1024];
            int read;
            while ((read = input.read(buffer)) >= 0) {
                if (read == 0) continue;
                output.write(buffer, 0, read);
                if (digest != null) digest.update(buffer, 0, read);
            }
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
