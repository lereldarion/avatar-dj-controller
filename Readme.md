# Test of an Avatar-side DJ controller system
Strategy : 
- Skinned mesh using physbone / contacts for interaction
- Extract slider/etc positions to pixels on the desktop view
- Rust deamon to read the pixels and push midi events to whatever
- NDMF script with AAC to process the DJ pad, generate the tracking mesh, and a JSON descriptor