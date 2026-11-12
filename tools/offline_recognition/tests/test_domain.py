import unittest

from ..domain import RecognitionError, is_disabled, load_data, select_operators


class RuleTests(unittest.TestCase):
    def setUp(self):
        self.bans = {"mainCovenant": ["谢拉格", "维多利亚"], "viceCovenant": ["坚守", "奥术"]}

    def op(self, main=(), vice=()):
        return {"name": "测试干员", "mainCovenant": list(main), "viceCovenant": list(vice)}

    def test_nonempty_main_has_priority_over_banned_vice(self):
        self.assertFalse(is_disabled(self.op(["炎"], ["坚守"]), self.bans))

    def test_all_main_banned_even_when_vice_available(self):
        self.assertTrue(is_disabled(self.op(["谢拉格", "维多利亚"], ["精准"]), self.bans))

    def test_one_main_available_is_enough(self):
        self.assertFalse(is_disabled(self.op(["谢拉格", "炎"]), self.bans))

    def test_no_main_and_all_vice_banned(self):
        self.assertTrue(is_disabled(self.op(vice=["坚守", "奥术"]), self.bans))

    def test_no_main_and_one_vice_available(self):
        self.assertFalse(is_disabled(self.op(vice=["坚守", "精准"]), self.bans))

    def test_both_empty_is_available(self):
        self.assertFalse(is_disabled(self.op(), self.bans))

    def test_unknown_ban_is_rejected(self):
        options, operators = load_data()
        with self.assertRaises(RecognitionError):
            select_operators(operators, {"mainCovenant": ["不存在"], "viceCovenant": []}, options)

    def test_operator_data_is_valid_and_complete(self):
        options, operators = load_data()
        self.assertEqual(len(operators), 141)
        self.assertEqual(len(options["mainCovenant"]), 10)
        self.assertEqual(len(options["viceCovenant"]), 16)
