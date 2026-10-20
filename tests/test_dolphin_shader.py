"""Negative controls for the rendered-TEV acceptance oracle."""
import sys
from pathlib import Path
import unittest
from unittest.mock import Mock,patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from dolphin_shader_test import CAPTURE_TIMEOUT_SECONDS,validate,validate_model,wait_for_capture


def frame():
    rgb=bytearray(640*480*3)
    colors=((12,24,64),(30,90,158),(70,220,248))
    for x in range(34,191):
        intensity=round((x-34)*255/156)
        for shift,value in ((0,(intensity,)*3),(208,(50,110,150)),
                            (416,colors[0 if intensity<=80 else 1 if intensity<=180 else 2])):
            at=(347*640+x+shift)*3;rgb[at:at+3]=bytes(value)
    for y,color in ((240,colors[2]),(275,colors[1]),(320,colors[0])):
        at=(y*640+528)*3;rgb[at:at+3]=bytes(color)
    at=(411*640+33)*3;rgb[at:at+3]=bytes((231,239,252))
    for x,color in ((60,(40,60,80)),(140,(140,100,100)),(220,(100,160,220)),(300,(80,120,160)),(380,(220,40,20)),(480,(40,80,120)),(560,(40,80,120))):
        at=(362*640+x)*3;rgb[at:at+3]=bytes(color)
    return rgb


class ShaderCaptureTests(unittest.TestCase):
    def test_slow_ci_startup_can_finish_after_thirty_seconds(self):
        process=Mock();process.poll.return_value=None
        frames=Mock();capture=Mock();capture.stat.return_value.st_size=512*1024
        frames.glob.return_value=[capture]
        with patch('dolphin_shader_test.time.monotonic',side_effect=[0,31]):
            self.assertIs(capture,wait_for_capture(process,frames,CAPTURE_TIMEOUT_SECONDS))

    def test_capture_deadline_remains_bounded_and_reports_partial_files(self):
        process=Mock();frames=Mock();capture=Mock();capture.name='partial.avi'
        capture.stat.return_value.st_size=1024;frames.glob.return_value=[capture]
        with patch('dolphin_shader_test.time.monotonic',side_effect=[0,91]):
            with self.assertRaisesRegex(TimeoutError,'partial.avi.*1024'):
                wait_for_capture(process,frames,CAPTURE_TIMEOUT_SECONDS)

    def test_emulator_exit_fails_without_waiting_for_deadline(self):
        process=Mock();process.poll.return_value=1
        with patch('dolphin_shader_test.time.monotonic',side_effect=[0,1]):
            with self.assertRaisesRegex(RuntimeError,'exited before rendering'):
                wait_for_capture(process,Mock(),CAPTURE_TIMEOUT_SECONDS)

    def test_three_real_effect_signatures(self):
        self.assertEqual([0,1,2],validate(frame())['toonBands'])

    def test_missing_each_preset_or_model_override_fails(self):
        for x in (60,140,220,300,380,480,560):
            rgb=frame();at=(362*640+x)*3;rgb[at:at+3]=bytes((8,13,26))
            with self.assertRaises(AssertionError):validate(rgb)

    def test_boot_or_clear_only_cannot_pass(self):
        with self.assertRaises(AssertionError):validate(bytes(640*480*3))

    def test_disabled_toon_stages_cannot_pass(self):
        rgb=frame()
        for x in range(34,191):
            source=(347*640+x)*3;target=source+416*3
            rgb[target:target+3]=rgb[source:source+3]
        with self.assertRaises(AssertionError):validate(rgb)

    def test_missing_ramp_cannot_pass(self):
        rgb=frame();at=(347*640+320)*3;rgb[at:at+3]=bytes((128,128,128))
        with self.assertRaises(AssertionError):validate(rgb)

    def test_incomplete_capture_cannot_pass(self):
        with self.assertRaises(ValueError):validate(b'')

    def test_missing_preset_cannot_pass(self):
        rgb=frame();at=(275*640+528)*3;rgb[at:at+3]=bytes((128,128,128))
        with self.assertRaises(AssertionError):validate(rgb)

    def test_leaked_hud_effect_cannot_pass(self):
        rgb=frame();at=(411*640+33)*3;rgb[at:at+3]=bytes((70,220,248))
        with self.assertRaises(AssertionError):validate(rgb)


class ImportedModelCaptureTests(unittest.TestCase):
    def model_frame(self,effect=True):
        rgb=frame()
        for shift in (0,208,416):
            custom=effect and shift==208
            for x,color in ((40,(8,13,26)),(80,(70,220,248) if custom else (204,204,204)),
                            (140,(30,90,158) if custom else (102,102,102))):
                at=(116*640+x+shift)*3;rgb[at:at+3]=bytes(color)
        return rgb
    def test_imported_default_effect_default_signatures(self):
        validate_model(self.model_frame(),True)
        validate_model(self.model_frame(False),False)
    def test_missing_override_fails(self):
        with self.assertRaises(AssertionError):validate_model(self.model_frame(False))
    def test_effect_leaking_to_subsequent_default_draw_fails(self):
        rgb=self.model_frame();at=(116*640+496)*3;rgb[at:at+3]=bytes((70,220,248))
        with self.assertRaises(AssertionError):validate_model(rgb)
    def test_disabling_texture_for_raster_effect_fails(self):
        rgb=self.model_frame();at=(116*640+248)*3;rgb[at:at+3]=bytes((70,220,248))
        with self.assertRaises(AssertionError):validate_model(rgb)
    def test_only_first_material_getting_effect_fails(self):
        rgb=self.model_frame();at=(116*640+348)*3;rgb[at:at+3]=bytes((102,102,102))
        with self.assertRaises(AssertionError):validate_model(rgb)


class SharedCaptureTests(unittest.TestCase):
    def test_model_cycle_is_explicitly_opted_in_by_shader_entry(self):
        import inspect
        import dolphin_shader_test as capture
        self.assertFalse(inspect.signature(capture.run).parameters['imported_models'].default)
        source=Path(capture.__file__).read_text()
        self.assertIn('run(parser.parse_args(),imported_models=True)',source)


class FrameConversionTests(unittest.TestCase):
    def test_conversion_has_bounded_workers_and_deadline(self):
        import dolphin_shader_test as capture
        from tempfile import TemporaryDirectory
        with TemporaryDirectory() as directory:
            log=Path(directory)/'decode.log'
            with patch.object(capture.subprocess,'run',return_value=Mock(stdout=b'pixels',stderr=b'')) as run:
                self.assertEqual(b'pixels',capture.run_ffmpeg(['-i','capture.avi','pipe:1'],log))
            command=run.call_args.args[0]
            self.assertEqual(['1','1'],[command[i+1] for i,arg in enumerate(command) if arg=='-threads'])
            self.assertEqual('1',command[command.index('-filter_threads')+1])
            self.assertEqual(30,run.call_args.kwargs['timeout'])

    def test_timeout_preserves_decoder_diagnostics(self):
        import dolphin_shader_test as capture
        from tempfile import TemporaryDirectory
        import subprocess
        with TemporaryDirectory() as directory:
            log=Path(directory)/'decode.log'
            with patch.object(capture.subprocess,'run',side_effect=subprocess.TimeoutExpired('ffmpeg',30,stderr=b'decoder stalled')):
                with self.assertRaisesRegex(RuntimeError,'decoder stalled'):
                    capture.run_ffmpeg(['-i','capture.avi','pipe:1'],log)
            self.assertEqual('decoder stalled',log.read_text())

    def test_incomplete_decoded_frames_fail_before_pixel_checks(self):
        import dolphin_shader_test as capture
        for raw in (b'',bytes(640*480*3),bytes(640*480*3*2+1)):
            with self.subTest(length=len(raw)),patch.object(capture,'run_ffmpeg',return_value=raw):
                with self.assertRaisesRegex(ValueError,'Incomplete capture'):
                    capture.decode_capture(Path('partial.avi'),Path('decode.log'))

    def test_real_video_round_trip_and_invalid_input(self):
        import dolphin_shader_test as capture
        import shutil
        from tempfile import TemporaryDirectory
        if not shutil.which('ffmpeg'):self.skipTest('FFmpeg not installed')
        with TemporaryDirectory() as directory:
            root=Path(directory);video=root/'capture.avi'
            capture.run_ffmpeg(['-f','rawvideo','-pix_fmt','rgb24','-s','32x24',
                                '-i','pipe:0','-c:v','ffv1','-y',str(video)],
                               root/'encode.log',input=bytes((20,80,160))*32*24*8)
            frames=capture.decode_capture(video,root/'decode.log')
            self.assertEqual(6,len(frames))
            self.assertEqual(bytes((20,80,160)),frames[1][:3])
            invalid=root/'invalid.avi';invalid.write_bytes(b'not a video')
            with self.assertRaisesRegex(RuntimeError,'FFmpeg failed'):
                capture.decode_capture(invalid,root/'invalid.log')
            self.assertTrue((root/'invalid.log').read_text())
