"""Controller injection contracts; actual input assertions execute inside the DOL."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from dolphin_controller_test import BUTTONS, cases, commands, pad_config


class ControllerInjectionTests(unittest.TestCase):
    def test_every_port_receives_every_button_and_analog_axis(self):
        sequence=list(cases())
        self.assertEqual(285,len(sequence))
        for port in range(4):
            selected=[case for case in sequence if case['port']==port]
            for button in BUTTONS:
                self.assertEqual(2,sum(case.get('buttons')==[button] for case in selected))
            for axis in range(6):
                self.assertEqual(5,sum(case.get('axis')==axis for case in selected))

    def test_other_ports_are_neutral_even_during_chords_and_diagonals(self):
        for case in cases():
            for port in range(4):
                if case['port']!=port:
                    text=commands(case,port)
                    self.assertNotIn('PRESS ',text)
                    self.assertIn('SET MAIN 0.5 0.5\nSET C 0.5 0.5',text)
                    self.assertIn('SET L 0\nSET R 0',text)

    def test_trigger_axes_do_not_inject_digital_clicks(self):
        text=commands(dict(port=0,axis=4,value=.75),0)
        self.assertIn('RELEASE L\n',text)
        self.assertIn('SET L 0.75\n',text)
        self.assertIn('SET R 0\n',text)

    def test_all_four_pad_profiles_use_independent_pipe_devices(self):
        text=pad_config()
        for port in range(4):
            self.assertIn(f'[GCPad{port+1}]\nDevice = Pipe/0/dnd{port}\n',text)
        self.assertEqual(4,text.count('Triggers/L-Analog = `Axis L +`'))
        self.assertEqual(4,text.count('Triggers/Dead Zone = 0.0'))


if __name__=='__main__': unittest.main()
