# Latest execution tail

## Exchange 1 — user

Ran the N3 Quest APK and reported a first-start UI stall, epoch 1 with no visible
scan/readout, then exercised stop/start. Asked whether unresolved outcomes,
readbacks, dispatch shape and renderer behaviour were correct, while requiring no
serial shader or dispatch-zoo regression.

## Exchange 1 — assistant

Device evidence separated the issues. The fixed native close is nine dispatches
and the complete profiled scan submission is 13 dispatches/11 entry points with
0.670 ms unresolved-path GPU work. The original 19-second stall and eight-frame
`RingExhausted` came from eager 1024 MiB carrier allocation plus unresolved records
retaining full capture leases. N3 now allocates one two-page pair, copies a 272-byte
exact unresolved record and releases capture textures after the fence/readback
issue. Unity Vulkan passes 65/65 and the rebuilt APK is installed.

## Exchange 2 — user

Confirmed stop/start and asked why no readout was visible, whether the log showed
that there was actually nothing to display, and requested final analysis, device
logs, commit and push.

## Exchange 2 — assistant

The second device interval ran revisions 582 through 840 with no ring exhaustion
or fault; the first interval published one full-S16 delta/root at revision 9. N3
therefore scans and publishes, but owns only one base-density bootstrap sample.
The temporary preview still consumes the pre-N6 GeometryG projection, which is not
equivalent to the new Merkaba tangent lift and is degenerate on two selected rows.
Thus an empty-looking preview is not evidence of a missing root. N6 owns the
generated eye/readout cutover; N3 is being checkpointed without inventing an
interim geometry authority.
