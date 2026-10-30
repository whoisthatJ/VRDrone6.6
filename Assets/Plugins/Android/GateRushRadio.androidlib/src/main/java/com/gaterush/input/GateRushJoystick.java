package com.gaterush.input;

import android.app.Activity;
import android.content.Context;
import android.hardware.input.InputManager;
import android.view.ActionMode;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.Menu;
import android.view.MenuItem;
import android.view.MotionEvent;
import android.view.SearchEvent;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.view.accessibility.AccessibilityEvent;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.List;

/**
 * Raw joystick reader for RC radios (GateRush VR).
 *
 * Wraps the activity window's Window.Callback, so it sees every joystick MotionEvent and gamepad KeyEvent
 * before Unity does (works with GameActivity and UnityPlayerActivity, no manifest entry-point change),
 * then passes the event on unchanged.
 *
 * Values are written into a direct ByteBuffer that C# reads through a pointer. Each slot is guarded by a
 * sequence counter (odd while writing), so the reader never blocks and never sees a torn update.
 *
 * Slot layout (4-byte ints/floats, native byte order):
 *   [0] sequence  [1] deviceId (0 = empty)  [2] vendorId  [3] productId
 *   [4] axis mask bits 0-31  [5] axis mask bits 32-63  [6] button mask  [7] reserved
 *   [8 .. 8+MAX_AXES) axis values indexed by MotionEvent.AXIS_* constant
 */
public final class GateRushJoystick {
    public static final int MAX_DEVICES = 4;
    public static final int MAX_AXES = 48; // AXIS_GENERIC_16 == 47
    public static final int HEADER_INTS = 8;
    public static final int SLOT_INTS = HEADER_INTS + MAX_AXES;

    // Gamepad/joystick keycodes mapped to button bits 0..31.
    private static final int[] BUTTON_KEYCODES = {
        KeyEvent.KEYCODE_BUTTON_A, KeyEvent.KEYCODE_BUTTON_B, KeyEvent.KEYCODE_BUTTON_C, KeyEvent.KEYCODE_BUTTON_X,
        KeyEvent.KEYCODE_BUTTON_Y, KeyEvent.KEYCODE_BUTTON_Z, KeyEvent.KEYCODE_BUTTON_L1, KeyEvent.KEYCODE_BUTTON_R1,
        KeyEvent.KEYCODE_BUTTON_L2, KeyEvent.KEYCODE_BUTTON_R2, KeyEvent.KEYCODE_BUTTON_THUMBL, KeyEvent.KEYCODE_BUTTON_THUMBR,
        KeyEvent.KEYCODE_BUTTON_START, KeyEvent.KEYCODE_BUTTON_SELECT, KeyEvent.KEYCODE_BUTTON_MODE, KeyEvent.KEYCODE_BUTTON_1,
        KeyEvent.KEYCODE_BUTTON_2, KeyEvent.KEYCODE_BUTTON_3, KeyEvent.KEYCODE_BUTTON_4, KeyEvent.KEYCODE_BUTTON_5,
        KeyEvent.KEYCODE_BUTTON_6, KeyEvent.KEYCODE_BUTTON_7, KeyEvent.KEYCODE_BUTTON_8, KeyEvent.KEYCODE_BUTTON_9,
        KeyEvent.KEYCODE_BUTTON_10, KeyEvent.KEYCODE_BUTTON_11, KeyEvent.KEYCODE_BUTTON_12, KeyEvent.KEYCODE_BUTTON_13,
        KeyEvent.KEYCODE_BUTTON_14, KeyEvent.KEYCODE_BUTTON_15, KeyEvent.KEYCODE_BUTTON_16, KeyEvent.KEYCODE_DPAD_CENTER,
    };

    private static ByteBuffer buffer;
    private static final String[] slotNames = new String[MAX_DEVICES];
    private static boolean installed;

    private GateRushJoystick() { }

    /** Installs the window hook (once) and returns the shared buffer. Call from Unity's main thread. */
    public static synchronized ByteBuffer install(final Activity activity) {
        if (buffer == null) {
            buffer = ByteBuffer.allocateDirect(MAX_DEVICES * SLOT_INTS * 4).order(ByteOrder.nativeOrder());
        }
        if (!installed && activity != null) {
            installed = true;
            activity.runOnUiThread(new Runnable() {
                @Override public void run() {
                    Window w = activity.getWindow();
                    w.setCallback(new Hook(w.getCallback()));
                    InputManager im = (InputManager) activity.getSystemService(Context.INPUT_SERVICE);
                    if (im != null) im.registerInputDeviceListener(new DeviceListener(), null);
                    for (int id : InputDevice.getDeviceIds()) {
                        InputDevice d = InputDevice.getDevice(id);
                        if (isJoystick(d)) slotFor(id);
                    }
                }
            });
        }
        return buffer;
    }

    public static synchronized String getDeviceName(int slot) {
        return slot >= 0 && slot < MAX_DEVICES && slotNames[slot] != null ? slotNames[slot] : "";
    }

    private static boolean isJoystick(InputDevice d) {
        return d != null && (d.getSources() & InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK;
    }

    private static int base(int slot) { return slot * SLOT_INTS * 4; }

    /** Finds or claims the slot for a device; returns -1 if all slots are taken. */
    private static synchronized int slotFor(int deviceId) {
        int free = -1;
        for (int s = 0; s < MAX_DEVICES; s++) {
            int id = buffer.getInt(base(s) + 4);
            if (id == deviceId) return s;
            if (id == 0 && free < 0) free = s;
        }
        if (free < 0) return -1;

        InputDevice d = InputDevice.getDevice(deviceId);
        long mask = 0;
        if (d != null) {
            List<InputDevice.MotionRange> ranges = d.getMotionRanges();
            for (InputDevice.MotionRange r : ranges) {
                if ((r.getSource() & InputDevice.SOURCE_CLASS_JOYSTICK) != 0 && r.getAxis() < MAX_AXES) mask |= 1L << r.getAxis();
            }
        }
        int b = base(free);
        begin(b);
        buffer.putInt(b + 4, deviceId);
        buffer.putInt(b + 8, d != null ? d.getVendorId() : 0);
        buffer.putInt(b + 12, d != null ? d.getProductId() : 0);
        buffer.putInt(b + 16, (int) (mask & 0xffffffffL));
        buffer.putInt(b + 20, (int) (mask >>> 32));
        buffer.putInt(b + 24, 0);
        for (int a = 0; a < MAX_AXES; a++) buffer.putFloat(b + (HEADER_INTS + a) * 4, 0f);
        end(b);
        slotNames[free] = d != null ? d.getName() : "Joystick " + deviceId;
        return free;
    }

    private static synchronized void release(int deviceId) {
        for (int s = 0; s < MAX_DEVICES; s++) {
            int b = base(s);
            if (buffer.getInt(b + 4) != deviceId) continue;
            begin(b);
            buffer.putInt(b + 4, 0);
            end(b);
            slotNames[s] = null;
        }
    }

    // Sequence counter: odd while a write is in progress.
    private static void begin(int b) { buffer.putInt(b, buffer.getInt(b) + 1); }
    private static void end(int b) { buffer.putInt(b, buffer.getInt(b) + 1); }

    static void onMotion(MotionEvent ev) {
        if ((ev.getSource() & InputDevice.SOURCE_JOYSTICK) != InputDevice.SOURCE_JOYSTICK || ev.getAction() != MotionEvent.ACTION_MOVE) return;
        synchronized (GateRushJoystick.class) {
            int slot = slotFor(ev.getDeviceId());
            if (slot < 0) return;
            int b = base(slot);
            long mask = (buffer.getInt(b + 16) & 0xffffffffL) | ((long) buffer.getInt(b + 20) << 32);
            begin(b);
            for (int a = 0; a < MAX_AXES; a++) {
                if ((mask & (1L << a)) != 0) buffer.putFloat(b + (HEADER_INTS + a) * 4, ev.getAxisValue(a));
            }
            end(b);
        }
    }

    static void onKey(KeyEvent ev) {
        int bit = -1;
        for (int i = 0; i < BUTTON_KEYCODES.length; i++) if (BUTTON_KEYCODES[i] == ev.getKeyCode()) { bit = i; break; }
        if (bit < 0) return;
        int src = ev.getSource();
        if ((src & InputDevice.SOURCE_JOYSTICK) != InputDevice.SOURCE_JOYSTICK && (src & InputDevice.SOURCE_GAMEPAD) != InputDevice.SOURCE_GAMEPAD) return;
        synchronized (GateRushJoystick.class) {
            int slot = slotFor(ev.getDeviceId());
            if (slot < 0) return;
            int b = base(slot);
            int buttons = buffer.getInt(b + 24);
            if (ev.getAction() == KeyEvent.ACTION_DOWN) buttons |= 1 << bit;
            else if (ev.getAction() == KeyEvent.ACTION_UP) buttons &= ~(1 << bit);
            begin(b);
            buffer.putInt(b + 24, buttons);
            end(b);
        }
    }

    private static final class DeviceListener implements InputManager.InputDeviceListener {
        @Override public void onInputDeviceAdded(int deviceId) {
            if (isJoystick(InputDevice.getDevice(deviceId))) slotFor(deviceId);
        }
        @Override public void onInputDeviceRemoved(int deviceId) { release(deviceId); }
        @Override public void onInputDeviceChanged(int deviceId) {
            release(deviceId);
            if (isJoystick(InputDevice.getDevice(deviceId))) slotFor(deviceId);
        }
    }

    /** Pass-through Window.Callback that peeks at joystick events. */
    private static final class Hook implements Window.Callback {
        private final Window.Callback inner;

        Hook(Window.Callback inner) { this.inner = inner; }

        @Override public boolean dispatchGenericMotionEvent(MotionEvent event) {
            onMotion(event);
            return inner.dispatchGenericMotionEvent(event);
        }

        @Override public boolean dispatchKeyEvent(KeyEvent event) {
            onKey(event);
            return inner.dispatchKeyEvent(event);
        }

        @Override public boolean dispatchKeyShortcutEvent(KeyEvent event) { return inner.dispatchKeyShortcutEvent(event); }
        @Override public boolean dispatchTouchEvent(MotionEvent event) { return inner.dispatchTouchEvent(event); }
        @Override public boolean dispatchTrackballEvent(MotionEvent event) { return inner.dispatchTrackballEvent(event); }
        @Override public boolean dispatchPopulateAccessibilityEvent(AccessibilityEvent event) { return inner.dispatchPopulateAccessibilityEvent(event); }
        @Override public View onCreatePanelView(int featureId) { return inner.onCreatePanelView(featureId); }
        @Override public boolean onCreatePanelMenu(int featureId, Menu menu) { return inner.onCreatePanelMenu(featureId, menu); }
        @Override public boolean onPreparePanel(int featureId, View view, Menu menu) { return inner.onPreparePanel(featureId, view, menu); }
        @Override public boolean onMenuOpened(int featureId, Menu menu) { return inner.onMenuOpened(featureId, menu); }
        @Override public boolean onMenuItemSelected(int featureId, MenuItem item) { return inner.onMenuItemSelected(featureId, item); }
        @Override public void onWindowAttributesChanged(WindowManager.LayoutParams attrs) { inner.onWindowAttributesChanged(attrs); }
        @Override public void onContentChanged() { inner.onContentChanged(); }
        @Override public void onWindowFocusChanged(boolean hasFocus) { inner.onWindowFocusChanged(hasFocus); }
        @Override public void onAttachedToWindow() { inner.onAttachedToWindow(); }
        @Override public void onDetachedFromWindow() { inner.onDetachedFromWindow(); }
        @Override public void onPanelClosed(int featureId, Menu menu) { inner.onPanelClosed(featureId, menu); }
        @Override public boolean onSearchRequested() { return inner.onSearchRequested(); }
        @Override public boolean onSearchRequested(SearchEvent searchEvent) { return inner.onSearchRequested(searchEvent); }
        @Override public ActionMode onWindowStartingActionMode(ActionMode.Callback callback) { return inner.onWindowStartingActionMode(callback); }
        @Override public ActionMode onWindowStartingActionMode(ActionMode.Callback callback, int type) { return inner.onWindowStartingActionMode(callback, type); }
        @Override public void onActionModeStarted(ActionMode mode) { inner.onActionModeStarted(mode); }
        @Override public void onActionModeFinished(ActionMode mode) { inner.onActionModeFinished(mode); }
    }
}
